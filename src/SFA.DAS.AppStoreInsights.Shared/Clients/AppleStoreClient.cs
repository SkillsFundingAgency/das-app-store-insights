using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
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

            _logger.LogInformation("AppleStoreClient initialized with BaseAddress: {BaseAddress}", _httpClient.BaseAddress);
        }

        private async Task<string> GetJwtTokenAsync()
        {
            if (_cachedJwt != null && DateTime.UtcNow < _jwtExpiry)
            {
                _logger.LogDebug("Using cached JWT");
                return _cachedJwt;
            }

            _logger.LogInformation("Generating new JWT token for Apple API");

            ECPrivateKeyParameters privateKey;
            using (var reader = new StringReader(_privateKeyPem))
            {
                var pemReader = new PemReader(reader);
                var pemObject = pemReader.ReadObject();

                if (pemObject is AsymmetricCipherKeyPair keyPair)
                {
                    privateKey = (ECPrivateKeyParameters)keyPair.Private;
                }
                else if (pemObject is ECPrivateKeyParameters ecPrivate)
                {
                    privateKey = ecPrivate;
                }
                else
                {
                    throw new InvalidOperationException("Unsupported PEM object type.");
                }
            }

            var now = DateTime.UtcNow;
            var header = new Dictionary<string, object>
            {
                { "alg", "ES256" },
                { "kid", _keyId },
                { "typ", "JWT" }
            };

            var payload = new Dictionary<string, object>
            {
                { "iss", _issuerId },
                { "iat", new DateTimeOffset(now).ToUnixTimeSeconds() },
                { "exp", new DateTimeOffset(now.AddMinutes(15)).ToUnixTimeSeconds() },
                { "aud", "appstoreconnect-v1" }
            };

            string headerJson = JsonSerializer.Serialize(header);
            string payloadJson = JsonSerializer.Serialize(payload);
            string headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            string payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            string message = $"{headerBase64}.{payloadBase64}";
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            var signer = new ECDsaSigner(new Org.BouncyCastle.Crypto.Signers.HMacDsaKCalculator(new Org.BouncyCastle.Crypto.Digests.Sha256Digest()));
            signer.Init(true, privateKey);

            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(messageBytes);
            }

            var signature = signer.GenerateSignature(hash);
            var rBytes = signature[0].ToByteArrayUnsigned();
            var sBytes = signature[1].ToByteArrayUnsigned();

            byte[] rPadded = new byte[32];
            byte[] sPadded = new byte[32];
            Array.Copy(rBytes, Math.Max(0, rBytes.Length - 32), rPadded, Math.Max(0, 32 - rBytes.Length), Math.Min(rBytes.Length, 32));
            Array.Copy(sBytes, Math.Max(0, sBytes.Length - 32), sPadded, Math.Max(0, 32 - sBytes.Length), Math.Min(sBytes.Length, 32));

            byte[] signatureBytes = new byte[64];
            Array.Copy(rPadded, 0, signatureBytes, 0, 32);
            Array.Copy(sPadded, 0, signatureBytes, 32, 32);

            string signatureBase64 = Base64UrlEncode(signatureBytes);

            _cachedJwt = $"{headerBase64}.{payloadBase64}.{signatureBase64}";
            _jwtExpiry = now.AddMinutes(15);

            _logger.LogInformation("JWT token generated successfully. Expires at {Expiry}", _jwtExpiry);
            return _cachedJwt;
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            string base64 = Convert.ToBase64String(bytes);
            return base64.Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        public async Task<List<AppleStoreReview>> GetReviewsSinceAsync(string appAppleId, DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("GetReviewsSinceAsync called for AppId: {AppAppleId}, since: {SinceUtc}", appAppleId, sinceUtc);

            try
            {
                var token = await GetJwtTokenAsync();
                _logger.LogDebug("JWT token obtained, setting authorization header");

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var url = $"apps/{appAppleId}/customerReviews?limit=200&sort=-createdDate";
                _logger.LogInformation("Calling Apple API: {Url}", url);

                var allReviews = new List<AppleStoreReview>();
                bool shouldStop = false;

                while (!string.IsNullOrEmpty(url) && !shouldStop)
                {
                    _logger.LogDebug("Fetching page: {Url}", url);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetReviewsSinceAsync for AppId: {AppAppleId}", appAppleId);
                throw;
            }
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