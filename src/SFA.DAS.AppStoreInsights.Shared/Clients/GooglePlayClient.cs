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
                            Comment = userComment.Text?.Replace("\t","") ?? "",
                            ReviewDateUtc = reviewDate,
                            LastModifiedUtc = reviewDate,
                            DeviceInfo = userCommentObj.UserComment.DeviceMetadata.ProductName ?? "",
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

        public async Task PostResponseAsync(string appPackageName, string reviewId, string replyText, CancellationToken cancellationToken = default)
        {
            var replyRequest = new Google.Apis.AndroidPublisher.v3.Data.ReviewsReplyRequest { ReplyText = replyText };
            var request = _publisherService.Reviews.Reply(replyRequest, appPackageName, reviewId);
            await request.ExecuteAsync(cancellationToken);
        }
    }
}
