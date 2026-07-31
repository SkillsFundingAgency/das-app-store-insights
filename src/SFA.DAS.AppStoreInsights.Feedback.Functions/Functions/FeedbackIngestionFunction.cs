using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.Feedback.Functions
{
    [ExcludeFromCodeCoverage]
    public class FeedbackIngestionFunction
    {
        private readonly IAppStoreRepository _repo;
        private readonly IAppleStoreClient _appleClient;
        private readonly IGooglePlayClient _googleClient;
        private readonly ILogger _logger;
        private readonly ApplicationConfiguration _appConfig;

        public FeedbackIngestionFunction(
            IAppStoreRepository repo,
            IAppleStoreClient apple,
            IGooglePlayClient google,
            IOptions<ApplicationConfiguration> appConfig,
            ILoggerFactory loggerFactory)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _appleClient = apple ?? throw new ArgumentNullException(nameof(apple));
            _googleClient = google ?? throw new ArgumentNullException(nameof(google));

            if (appConfig == null)
                throw new ArgumentNullException(nameof(appConfig));

            _appConfig = appConfig.Value ?? throw new ArgumentNullException(nameof(appConfig));
            _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory)))
                .CreateLogger<FeedbackIngestionFunction>();
        }

        [Function("FetchAppStoreFeedback")]
        public async Task Run(
            [TimerTrigger("0 0 2 * * *", RunOnStartup = true)] TimerInfo timer,
            FunctionContext context)
        {
            var appId = await _repo.GetAppIdAsync("Apprentice App", CancellationToken.None);
            var yesterday = DateTime.UtcNow.AddYears(-3);

            string appleAppId = _appConfig.AppleAppId;
            string googlePackageName = _appConfig.GooglePackageName;

            // Apple
            var appleReviews = await _appleClient.GetReviewsSinceAsync(appleAppId, yesterday, CancellationToken.None);
            foreach (var review in appleReviews)
            {
                if (!await _repo.ReviewExistsAsync(1, review.ReviewId, CancellationToken.None))
                {
                    var mappedReview = new Review
                    {
                        AppId = appId,
                        VendorId = 1,
                        ExternalId = review.ReviewId,
                        ReviewerName = review.ReviewerName ?? "Anonymous",
                        Rating = (byte)review.Rating,
                        Title = review.Title ?? string.Empty,
                        Comment = review.Comment ?? string.Empty,
                        ReviewDate = review.ReviewDateUtc,
                        DeviceInfo = review.DeviceInfo ?? string.Empty
                    };
                    await _repo.InsertReviewAsync(mappedReview, CancellationToken.None);
                    _logger.LogInformation("Inserted Apple review {ExternalId}", review.ReviewId);
                }
            }

            // Google
            var googleReviews = await _googleClient.GetReviewsSinceAsync(googlePackageName, yesterday, CancellationToken.None);
            foreach (var review in googleReviews)
            {
                if (!await _repo.ReviewExistsAsync(2, review.ReviewId, CancellationToken.None))
                {
                    var mappedReview = new Review
                    {
                        AppId = appId,
                        VendorId = 2,
                        ExternalId = review.ReviewId,
                        ReviewerName = review.ReviewerName ?? "Anonymous",
                        Rating = (byte)review.Rating,
                        Title = review.Title ?? string.Empty,
                        Comment = review.Comment ?? string.Empty,
                        ReviewDate = review.ReviewDateUtc,
                        DeviceInfo = review.DeviceInfo ?? string.Empty
                    };
                    await _repo.InsertReviewAsync(mappedReview, CancellationToken.None);
                    _logger.LogInformation("Inserted Google review {ExternalId}", review.ReviewId);
                }
            }
        }
    }
}