using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private readonly StorageClient _storageClient;
        private readonly ILogger<GooglePlayClient> _logger;
        private readonly string _bucketName;
        private readonly GoogleCredential _credential;

        public GooglePlayClient(IConfiguration config, ILogger<GooglePlayClient> logger)
        {
            _logger = logger;
            var serviceAccountJson = config["Google:ServiceAccountJson"]
                ?? throw new InvalidOperationException("Google:ServiceAccountJson missing");

            _credential = GoogleCredential.FromJson(serviceAccountJson)
                .CreateScoped(
                    AndroidPublisherService.Scope.Androidpublisher,
                    "https://www.googleapis.com/auth/devstorage.read_only");

            _publisherService = new AndroidPublisherService(new BaseClientService.Initializer
            {
                HttpClientInitializer = _credential,
                ApplicationName = "AppStoreInsights"
            });

            _storageClient = StorageClient.Create(_credential);

            _bucketName = config["Google:StatsBucketName"]
                ?? throw new InvalidOperationException("Google:StatsBucketName missing");
        }

        public async Task<List<GooglePlayReview>> GetReviewsSinceAsync(
            string appPackageName,
            DateTime sinceUtc,
            CancellationToken cancellationToken = default)
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
                            Comment = userComment.Text?.Replace("\t", "") ?? "",
                            ReviewDateUtc = reviewDate,
                            LastModifiedUtc = reviewDate,
                            DeviceInfo = userCommentObj.UserComment.DeviceMetadata?.ProductName ?? "",
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

        public async Task PostResponseAsync(
            string appPackageName,
            string reviewId,
            string replyText,
            CancellationToken cancellationToken = default)
        {
            var replyRequest = new Google.Apis.AndroidPublisher.v3.Data.ReviewsReplyRequest { ReplyText = replyText };
            var request = _publisherService.Reviews.Reply(replyRequest, appPackageName, reviewId);
            await request.ExecuteAsync(cancellationToken);
        }

        public async Task<List<GooglePlayUsageMetric>> GetDailyMetricsAsync(
            string appPackageName,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Fetching Google Play metrics from monthly overview files for {Start} to {End}",
                startDate, endDate);

            var metrics = new List<GooglePlayUsageMetric>();

            var currentMonth = new DateOnly(startDate.Year, startDate.Month, 1);
            var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);

            while (currentMonth <= endMonth)
            {
                var monthStr = currentMonth.ToString("yyyyMM");
                string objectName = $"stats/installs/installs_{appPackageName}_{monthStr}_overview.csv";

                _logger.LogDebug("Looking for file: {ObjectName}", objectName);

                try
                {
                    var stream = new MemoryStream();
                    await _storageClient.DownloadObjectAsync(_bucketName, objectName, stream,
                        cancellationToken: cancellationToken);
                    stream.Position = 0;

                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    var csvContent = await reader.ReadToEndAsync(cancellationToken);

                    var lines = csvContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length < 2)
                    {
                        _logger.LogWarning("No data rows in {ObjectName}", objectName);
                        currentMonth = currentMonth.AddMonths(1);
                        continue;
                    }

                    var header = lines[0].Split(',');
                    int dateIndex = Array.IndexOf(header, "Date");
                    int packageIndex = Array.IndexOf(header, "Package name");
                    int deviceInstallsIndex = Array.IndexOf(header, "Daily Device Installs");
                    int userInstallsIndex = Array.IndexOf(header, "Daily User Installs");
                    int uninstallEventsIndex = Array.IndexOf(header, "Uninstall events");
                    int activeDeviceInstallsIndex = Array.IndexOf(header, "Active Device Installs");

                    if (deviceInstallsIndex == -1) deviceInstallsIndex = Array.IndexOf(header, "Installs");
                    if (userInstallsIndex == -1) userInstallsIndex = Array.IndexOf(header, "Downloads");
                    if (uninstallEventsIndex == -1) uninstallEventsIndex = Array.IndexOf(header, "Uninstalls");
                    if (activeDeviceInstallsIndex == -1) activeDeviceInstallsIndex = Array.IndexOf(header, "Active Device Installs");

                    if (dateIndex == -1 || packageIndex == -1 || deviceInstallsIndex == -1 || uninstallEventsIndex == -1)
                    {
                        _logger.LogWarning("Required columns not found in {ObjectName}. Header: {Header}",
                            objectName, string.Join(",", header));
                        currentMonth = currentMonth.AddMonths(1);
                        continue;
                    }

                    for (int i = 1; i < lines.Length; i++)
                    {
                        var columns = lines[i].Split(',');
                        if (columns.Length <= Math.Max(dateIndex, Math.Max(packageIndex,
                                Math.Max(deviceInstallsIndex, Math.Max(uninstallEventsIndex, activeDeviceInstallsIndex)))))
                            continue;

                        var pkg = columns[packageIndex].Trim('\"');
                        if (pkg != appPackageName)
                            continue;

                        var dateStr = columns[dateIndex].Trim('\"');
                        if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                DateTimeStyles.None, out var metricDate))
                        {
                            if (!DateOnly.TryParse(dateStr, out metricDate))
                                continue;
                        }

                        if (metricDate < startDate || metricDate > endDate)
                            continue;

                        int userInstalls = 0, activeDeviceInstalls = 0;
                        if (userInstallsIndex != -1 && userInstallsIndex < columns.Length)
                            int.TryParse(columns[userInstallsIndex].Trim('\"'), out userInstalls);
                        if (activeDeviceInstallsIndex != -1 && activeDeviceInstallsIndex < columns.Length)
                            int.TryParse(columns[activeDeviceInstallsIndex].Trim('\"'), out activeDeviceInstalls);

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            file = objectName,
                            row = lines[i],
                            headers = header
                        });

                        metrics.Add(new GooglePlayUsageMetric
                        {
                            Date = metricDate,
                            Downloads = userInstalls,
                            DailyActiveUsers = activeDeviceInstalls,
                            RawDataJson = rawJson
                        });

                        _logger.LogDebug("Fetched metric for {Date}: D={Downloads}, A={DailyActiveUsers}",
                            metricDate, userInstalls, activeDeviceInstalls);
                    }
                }
                catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("File for month {Month} not found (not yet generated)", monthStr);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file for month {Month}", monthStr);
                }

                currentMonth = currentMonth.AddMonths(1);
            }

            metrics = metrics.OrderBy(m => m.Date).ToList();

            _logger.LogInformation("Retrieved {Count} metrics from GCS monthly files", metrics.Count);
            return metrics;
        }
    }
}