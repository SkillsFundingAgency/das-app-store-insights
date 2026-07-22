using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    [ExcludeFromCodeCoverage]
    public class GooglePlayClient : IGooglePlayClient
    {
        private readonly AndroidPublisherService _publisherService;
        private readonly HttpClient _reportingHttpClient;
        private readonly ILogger<GooglePlayClient> _logger;
        private readonly string _serviceAccountJson;
        private readonly GoogleCredential _credential;

        public GooglePlayClient(IConfiguration config, ILogger<GooglePlayClient> logger)
        {
            _logger = logger;
            _serviceAccountJson = config["Google:ServiceAccountJson"]
                ?? throw new InvalidOperationException("Google:ServiceAccountJson missing");

            _credential = GoogleCredential.FromJson(_serviceAccountJson)
                .CreateScoped(AndroidPublisherService.Scope.Androidpublisher,
                              "https://www.googleapis.com/auth/playdeveloperreporting");

            _publisherService = new AndroidPublisherService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = "AppStoreInsights"
            });

            // For Play Reporting API (separate base URL)
            _reportingHttpClient = new HttpClient();
            _reportingHttpClient.BaseAddress = new Uri("https://playdeveloperreporting.googleapis.com/v1beta1/");
            _reportingHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private async Task<string> GetAccessTokenAsync()
        {
            var token = await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
            return token;
        }

        public async Task<List<GooglePlayReview>> GetReviewsSinceAsync(string appPackageName, DateTime sinceUtc, CancellationToken cancellationToken = default)
        {
            var allReviews = new List<GooglePlayReview>();
            string pageToken = null;
            do
            {
                var request = _publisherService.Reviews.List(appPackageName);
                request.Token = pageToken;
                var response = await request.ExecuteAsync(cancellationToken);

                if (response.Reviews != null)
                {
                    foreach (var rev in response.Reviews)
                    {
                        var userCommentObj = rev.Comments?.FirstOrDefault(c => c.UserComment != null);
                        if (userCommentObj?.UserComment == null) continue;
                        var userComment = userCommentObj.UserComment;
                        var developerCommentObj = rev.Comments?.FirstOrDefault(c => c.DeveloperComment != null);
                        var developerComment = developerCommentObj?.DeveloperComment;

                        DateTime reviewDate = DateTime.UtcNow;
                        if (userComment.LastModified != null)
                            reviewDate = DateTimeOffset.FromUnixTimeSeconds(userComment.LastModified.Seconds ?? 0).UtcDateTime;

                        var review = new GooglePlayReview
                        {
                            ReviewId = rev.ReviewId,
                            ReviewerName = rev.AuthorName,
                            Rating = userComment.StarRating ?? 0,
                            Title = "",
                            Comment = userComment.Text ?? "",
                            ReviewDateUtc = reviewDate,
                            LastModifiedUtc = reviewDate,
                            DeviceInfo = userComment.Device ?? "",
                            DeveloperReply = developerComment != null
                                ? new GooglePlayDeveloperReply
                                {
                                    ReplyText = developerComment.Text,
                                    ReplyDateUtc = developerComment.LastModified != null
                                        ? DateTimeOffset.FromUnixTimeSeconds(developerComment.LastModified.Seconds ?? 0).UtcDateTime
                                        : DateTime.UtcNow,
                                    ReplyId = null
                                }
                                : null
                        };
                        allReviews.Add(review);
                    }
                }
                pageToken = response.TokenPagination?.NextPageToken;
            } while (!string.IsNullOrEmpty(pageToken));

            return allReviews.Where(r => r.LastModifiedUtc >= sinceUtc).ToList();
        }

        public async Task<List<GooglePlayUsageMetric>> GetDailyMetricsAsync(string appPackageName, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Fetching Google Play Reporting metrics for {Start} to {End}", startDate, endDate);

            var token = await GetAccessTokenAsync();
            _reportingHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var metrics = new List<GooglePlayUsageMetric>();
            var currentDate = startDate;
            while (currentDate <= endDate)
            {
                var metricForDay = await FetchSingleDayMetricsAsync(appPackageName, currentDate, cancellationToken);
                if (metricForDay != null)
                    metrics.Add(metricForDay);
                currentDate = currentDate.AddDays(1);
            }

            _logger.LogInformation("Retrieved {Count} daily metrics from Google Reporting API", metrics.Count);
            return metrics;
        }

        private async Task<GooglePlayUsageMetric> FetchSingleDayMetricsAsync(string packageName, DateOnly date, CancellationToken ct)
        {
            // Use the Acquisition Metrics API: https://developers.google.com/android-publisher/play-developer-reporting/acquisition
            // Endpoint: apps/{appPackageName}/acquisitionMetrics:query
            var url = $"apps/{packageName}/acquisitionMetrics:query";
            var requestBody = new
            {
                dimensions = new[] { "date" },
                metrics = new[] { "acquiredUsers", "totalUsers", "uninstallers", "activeUsers" },
                dateRange = new
                {
                    startDate = new { year = date.Year, month = date.Month, day = date.Day },
                    endDate = new { year = date.Year, month = date.Month, day = date.Day }
                }
            };
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _reportingHttpClient.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to fetch metrics for {Date}: {StatusCode} - {Error}", date, response.StatusCode, error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var rows = doc.RootElement.GetProperty("rows");
            if (rows.GetArrayLength() == 0) return null;

            var firstRow = rows[0];
            var metricsObj = firstRow.GetProperty("metrics");
            var acquiredUsers = GetMetricValue(metricsObj, "acquiredUsers");
            var totalUsers = GetMetricValue(metricsObj, "totalUsers");
            var uninstallers = GetMetricValue(metricsObj, "uninstallers");
            var activeUsers = GetMetricValue(metricsObj, "activeUsers");

            return new GooglePlayUsageMetric
            {
                Date = date,
                Downloads = acquiredUsers, // new users who installed the app
                Installs = totalUsers, // total installations (including re-installs)
                Uninstalls = uninstallers,
                DailyActiveUsers = activeUsers,
                Sessions = 0  // Not directly available; can be fetched from engagement metrics
            };
        }

        private int GetMetricValue(JsonElement metricsObj, string metricName)
        {
            if (metricsObj.TryGetProperty(metricName, out var metric))
            {
                return metric.TryGetProperty("int64Value", out var intVal) ? intVal.GetInt32() : 0;
            }
            return 0;
        }

        public async Task PostResponseAsync(string appPackageName, string reviewId, string replyText, CancellationToken cancellationToken = default)
        {
            var replyRequest = new Google.Apis.AndroidPublisher.v3.Data.ReviewsReplyRequest { ReplyText = replyText };
            var request = _publisherService.Reviews.Reply(replyRequest, appPackageName, reviewId);
            await request.ExecuteAsync(cancellationToken);
        }
    }
}