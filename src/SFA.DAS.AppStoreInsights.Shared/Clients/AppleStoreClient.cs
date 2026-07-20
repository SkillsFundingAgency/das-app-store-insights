using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    [ExcludeFromCodeCoverage]
    public class AppleStoreClient : IAppleStoreClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AppleStoreClient> _logger;
        private readonly string _privateKeyPem;
        private readonly string _issuerId;
        private readonly string _keyId;
        private readonly string _vendorNumber;

        private string _cachedJwt;
        private DateTime _jwtExpiry = DateTime.UtcNow;

        public AppleStoreClient(HttpClient httpClient, IConfiguration config, ILogger<AppleStoreClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _privateKeyPem = config["Apple:PrivateKey"] ?? throw new InvalidOperationException("Apple:PrivateKey missing");
            _issuerId = config["Apple:IssuerId"] ?? throw new InvalidOperationException("Apple:IssuerId missing");
            _keyId = config["Apple:KeyId"] ?? throw new InvalidOperationException("Apple:KeyId missing");
            _vendorNumber = config["Apple:VendorNumber"] ?? throw new InvalidOperationException("Apple:VendorNumber missing");

            _httpClient.BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<string> GetJwtTokenAsync()
        {
            if (_cachedJwt != null && DateTime.UtcNow < _jwtExpiry)
                return _cachedJwt;

            using var ecdsa = ECDsa.Create();
            var privateKeyBytes = ParsePkcs8PrivateKey(_privateKeyPem);
            ecdsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

            var handler = new JwtSecurityTokenHandler();
            var now = DateTime.UtcNow;
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = _issuerId,
                Audience = "appstoreconnect-v1",
                Expires = now.AddMinutes(15),
                IssuedAt = now,
                NotBefore = now,
                SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(ecdsa), SecurityAlgorithms.EcdsaSha256)
            };
            var token = handler.CreateJwtSecurityToken(tokenDescriptor);
            token.Header.Add("kid", _keyId);

            _cachedJwt = handler.WriteToken(token);
            _jwtExpiry = now.AddMinutes(15);
            return _cachedJwt;
        }

        private byte[] ParsePkcs8PrivateKey(string pem)
        {
            var base64 = pem
                .Replace("-----BEGIN PRIVATE KEY-----", "")
                .Replace("-----END PRIVATE KEY-----", "")
                .Replace("\n", "").Replace("\r", "").Replace(" ", "");
            return Convert.FromBase64String(base64);
        }

        public async Task<List<AppleStoreReview>> GetReviewsSinceAsync(string appAppleId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            var token = await GetJwtTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var url = $"apps/{appAppleId}/customerReviews?limit=200&sort=-createdDate";
            _logger.LogDebug("Apple reviews URL: {Url}", url);

            var allReviews = new List<AppleStoreReview>();
            bool shouldStop = false;

            while (!string.IsNullOrEmpty(url) && !shouldStop)
            {
                var response = await SendWithRetryAsync(() => _httpClient.GetAsync(url, cancellationToken));

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError("Apple API request failed: {StatusCode} - {Error}", response.StatusCode, errorBody);
                    response.EnsureSuccessStatusCode();
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = JsonSerializer.Deserialize<AppleReviewsResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed?.Data != null)
                {
                    foreach (var item in parsed.Data)
                    {
                        var reviewDate = item.Attributes?.CreatedDate ?? DateTime.UtcNow;

                        if (reviewDate < sinceUtc)
                        {
                            shouldStop = true;
                            break;
                        }

                        var review = new AppleStoreReview
                        {
                            ReviewId = item.Id,
                            ReviewerName = item.Attributes?.ReviewerNickname ?? "Anonymous",
                            Rating = item.Attributes?.Rating ?? 0,
                            Title = item.Attributes?.Title ?? "",
                            Comment = item.Attributes?.Body ?? "",
                            ReviewDateUtc = reviewDate,
                            LastModifiedUtc = item.Attributes?.LastModifiedDate ?? reviewDate,
                            DeviceInfo = $"{item.Attributes?.DeviceType} / {item.Attributes?.OsVersion}",
                            DeveloperReply = item.Attributes?.DeveloperResponse != null
                                ? new AppleStoreDeveloperReply
                                {
                                    ResponseText = item.Attributes.DeveloperResponse.ResponseBody,
                                    ResponseDateUtc = item.Attributes.DeveloperResponse.LastModifiedDate,
                                    ResponseId = item.Id
                                }
                                : null
                        };
                        allReviews.Add(review);
                    }
                }

                if (!shouldStop)
                {
                    url = parsed?.Links?.Next;
                    if (!string.IsNullOrEmpty(url) && !url.StartsWith("https"))
                        url = $"https://api.appstoreconnect.apple.com/v1/{url}";
                }
                else
                {
                    url = null;
                }
            }

            _logger.LogInformation("Retrieved {Count} Apple reviews since {Since}", allReviews.Count, sinceUtc);
            return allReviews;
        }

        public async Task<List<AppleStoreUsageMetric>> GetDailyMetricsAsync(string appAppleId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Fetching Apple Sales & Trends report for {Start} to {End}", startDate, endDate);

            var token = await GetJwtTokenAsync();
            var allMetrics = new List<AppleStoreUsageMetric>();
            var currentDate = startDate;

            while (currentDate <= endDate)
            {
                var dateStr = currentDate.ToString("yyyy-MM-dd");
                var query = $"salesReports?filter[reportType]=SALES&filter[reportSubType]=SUMMARY&filter[vendorNumber]={Uri.EscapeDataString(_vendorNumber)}&filter[reportDate]={dateStr}&filter[frequency]=DAILY";

                _logger.LogDebug("Fetching report for {Date}: {Query}", currentDate, query);

                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/a-gzip"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await SendWithRetryAsync(() => _httpClient.SendAsync(request, cancellationToken));

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Failed to get report for {Date}: {StatusCode} - {Error}", currentDate, response.StatusCode, error);
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                await using var compressedStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var decompressor = new GZipStream(compressedStream, CompressionMode.Decompress);
                using var reader = new StreamReader(decompressor, Encoding.UTF8);
                var tsvContent = await reader.ReadToEndAsync(cancellationToken);

                var dailyMetrics = ParseSalesReportTsv(tsvContent, currentDate, currentDate, appAppleId);
                allMetrics.AddRange(dailyMetrics);

                currentDate = currentDate.AddDays(1);
            }

            _logger.LogInformation("Fetched {Count} daily metrics from Apple", allMetrics.Count);
            return allMetrics;
        }

        private List<AppleStoreUsageMetric> ParseSalesReportTsv(string tsvContent, DateOnly startDate, DateOnly endDate, string appAppleId)
        {
            var metrics = new Dictionary<DateOnly, AppleStoreUsageMetric>();
            var lines = tsvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                return new List<AppleStoreUsageMetric>();

            var headers = lines[0].Split('\t', StringSplitOptions.None);
            var dateIndex = Array.IndexOf(headers, "Begin Date");
            if (dateIndex == -1)
                dateIndex = Array.IndexOf(headers, "End Date");
            var appIdIndex = Array.IndexOf(headers, "Apple Identifier");
            var unitsIndex = Array.IndexOf(headers, "Units");

            for (int i = 1; i < lines.Length; i++)
            {
                var columns = lines[i].Split('\t', StringSplitOptions.None);
                // Ensure we have enough columns for the indices we need
                if (columns.Length <= Math.Max(dateIndex, Math.Max(appIdIndex, unitsIndex)))
                    continue;

                var dateStr = columns[dateIndex];
                if (!DateOnly.TryParse(dateStr, out var date))
                    continue;

                var appIdFromReport = columns[appIdIndex];
                if (appIdFromReport != appAppleId)
                    continue;

                if (!int.TryParse(columns[unitsIndex], out var units))
                    continue;

                if (date >= startDate && date <= endDate)
                {
                    if (!metrics.TryGetValue(date, out var metric))
                    {
                        metric = new AppleStoreUsageMetric
                        {
                            Date = date,
                            Downloads = 0,
                            Installs = 0,
                            Uninstalls = 0,
                            Sessions = 0,
                            DailyActiveDevices = 0
                        };
                        metrics[date] = metric;
                    }
                    // Aggregate Units – we'll treat both Downloads and Installs as total units for now.
                    // If you need to separate new downloads from updates, use "Product Type Identifier" filter.
                    metric.Downloads += units;
                    metric.Installs += units;
                }
            }

            return metrics.Values.ToList();
        }

        public async Task PostResponseAsync(string appAppleId, string reviewId, string responseText, CancellationToken cancellationToken = default)
        {
            var token = await GetJwtTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                data = new
                {
                    type = "reviewResponses",
                    attributes = new { responseBody = responseText },
                    relationships = new
                    {
                        review = new
                        {
                            data = new { id = reviewId, type = "customerReviews" }
                        }
                    }
                }
            };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await SendWithRetryAsync(() => _httpClient.PostAsync("reviewResponses", content, cancellationToken));
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Posting reply failed: {StatusCode} - {Error}", response.StatusCode, error);
                response.EnsureSuccessStatusCode();
            }
        }

        private async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> action, int maxRetries = 3)
        {
            int retry = 0;
            while (true)
            {
                try
                {
                    var response = await action();
                    if (response.IsSuccessStatusCode || retry >= maxRetries)
                        return response;
                    if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        retry++;
                        await Task.Delay(1000 * retry);
                        continue;
                    }
                    return response;
                }
                catch (HttpRequestException) when (retry < maxRetries)
                {
                    retry++;
                    await Task.Delay(1000 * retry);
                }
            }
        }

        private class AppleReviewsResponse
        {
            public List<ReviewData> Data { get; set; }
            public Links Links { get; set; }
        }
        private class Links { public string Next { get; set; } }
        private class ReviewData
        {
            public string Id { get; set; }
            public ReviewAttributes Attributes { get; set; }
        }
        private class ReviewAttributes
        {
            public string Body { get; set; }
            public int Rating { get; set; }
            public string Title { get; set; }
            public string ReviewerNickname { get; set; }
            public DateTime CreatedDate { get; set; }
            public DateTime LastModifiedDate { get; set; }
            public string DeviceType { get; set; }
            public string OsVersion { get; set; }
            public DeveloperResponse DeveloperResponse { get; set; }
        }
        private class DeveloperResponse
        {
            public string ResponseBody { get; set; }
            public DateTime LastModifiedDate { get; set; }
        }
    }
}