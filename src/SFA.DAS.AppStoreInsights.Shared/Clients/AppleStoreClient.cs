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

            var since = sinceUtc.ToString("yyyy-MM-dd'T'HH:mm:ssZ");
            var url = $"customerReviews?filter[appAppleId]={appAppleId}&filter[lastModifiedDate]={since}&limit=200&sort=modifiedDate";
            var allReviews = new List<AppleStoreReview>();

            while (!string.IsNullOrEmpty(url))
            {
                var response = await SendWithRetryAsync(() => _httpClient.GetAsync(url, cancellationToken));
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = JsonSerializer.Deserialize<AppleReviewsResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed?.Data != null)
                {
                    foreach (var item in parsed.Data)
                    {
                        var review = new AppleStoreReview
                        {
                            ReviewId = item.Id,
                            ReviewerName = item.Attributes?.ReviewerNickname ?? "Anonymous",
                            Rating = item.Attributes?.Rating ?? 0,
                            Title = "",
                            Comment = item.Attributes?.Body ?? "",
                            ReviewDateUtc = item.Attributes?.CreatedDate ?? DateTime.UtcNow,
                            LastModifiedUtc = item.Attributes?.LastModifiedDate ?? DateTime.UtcNow,
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

                url = parsed?.Links?.Next;
                if (!string.IsNullOrEmpty(url) && !url.StartsWith("https"))
                    url = $"https://api.appstoreconnect.apple.com/v1/{url}";
            }

            return allReviews;
        }

        public async Task<List<AppleStoreUsageMetric>> GetDailyMetricsAsync(string appAppleId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Fetching Apple Sales & Trends report for {Start} to {End}", startDate, endDate);

            var token = await GetJwtTokenAsync();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var reportRequest = new
            {
                data = new
                {
                    type = "salesReports",
                    attributes = new
                    {
                        reportType = "SALES",
                        reportSubType = "SUMMARY",
                        vendorNumber = _vendorNumber,
                        reportDate = startDate.ToString("yyyy-MM-dd"),
                        frequency = "DAILY",
                        version = "1_0"
                    }
                }
            };
            var requestJson = JsonSerializer.Serialize(reportRequest);
            var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var createResponse = await SendWithRetryAsync(() => _httpClient.PostAsync("salesReports", requestContent, cancellationToken));

            if (!createResponse.IsSuccessStatusCode)
            {
                var error = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to request Apple sales report: {StatusCode} - {Error}", createResponse.StatusCode, error);
                throw new HttpRequestException($"Apple report request failed: {createResponse.StatusCode}");
            }
            
            var reportUrl = await PollForReportUrlAsync(createResponse, cancellationToken);

            var tsvData = await DownloadAndDecompressReportAsync(reportUrl, cancellationToken);

            var metrics = ParseSalesReportTsv(tsvData, startDate, endDate, appAppleId);
            _logger.LogInformation("Parsed {Count} daily metrics from Apple sales report", metrics.Count);
            return metrics;
        }

        private async Task<string> PollForReportUrlAsync(HttpResponseMessage initialResponse, CancellationToken ct)
        {
            var operationUrl = initialResponse.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(operationUrl))
                throw new InvalidOperationException("No operation URL returned from Apple report request");

            const int maxAttempts = 30;
            const int delaySeconds = 5;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var statusResponse = await SendWithRetryAsync(() => _httpClient.GetAsync(operationUrl, ct));
                var statusJson = await statusResponse.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(statusJson);
                var status = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("status").GetString();

                if (status == "COMPLETE")
                {
                    var reportUrl = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("url").GetString();
                    if (!string.IsNullOrEmpty(reportUrl))
                        return reportUrl;
                }
                else if (status == "FAILED")
                {
                    throw new Exception("Apple report generation failed");
                }
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
            throw new TimeoutException("Apple report generation timed out");
        }

        private async Task<string> DownloadAndDecompressReportAsync(string reportUrl, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, reportUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetJwtTokenAsync());
            var response = await SendWithRetryAsync(() => _httpClient.SendAsync(request, ct));
            response.EnsureSuccessStatusCode();

            await using var compressedStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressor = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var reader = new StreamReader(decompressor, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }

        private List<AppleStoreUsageMetric> ParseSalesReportTsv(string tsvContent, DateOnly startDate, DateOnly endDate, string appAppleId)
        {
            var metrics = new List<AppleStoreUsageMetric>();
            var lines = tsvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return metrics;

            // First line is header
            var headers = lines[0].Split('\t');
            var dateIndex = Array.IndexOf(headers, "Date");
            var appIdIndex = Array.IndexOf(headers, "Apple Identifier");
            var unitsIndex = Array.IndexOf(headers, "Units");
            var installsIndex = Array.IndexOf(headers, "Installments");
            // Note: Apple reports "Units" are first-time downloads; "Installments" are total installs.
            // Uninstalls and sessions are not available in this report.

            for (int i = 1; i < lines.Length; i++)
            {
                var columns = lines[i].Split('\t');
                if (columns.Length <= Math.Max(dateIndex, appIdIndex)) continue;

                var dateStr = columns[dateIndex];
                if (!DateOnly.TryParse(dateStr, out var date)) continue;

                var appIdFromReport = columns[appIdIndex];
                if (appIdFromReport != appAppleId) continue; // filter by app

                var downloads = unitsIndex >= 0 && int.TryParse(columns[unitsIndex], out var units) ? units : 0;
                var installs = installsIndex >= 0 && int.TryParse(columns[installsIndex], out var inst) ? inst : 0;

                if (date >= startDate && date <= endDate)
                {
                    metrics.Add(new AppleStoreUsageMetric
                    {
                        Date = date,
                        Downloads = downloads,
                        Installs = installs,
                        Uninstalls = 0, // not available for apple
                        Sessions = 0,   // not available for apple
                        DailyActiveDevices = 0
                    });
                }
            }
            return metrics;
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
            response.EnsureSuccessStatusCode();
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