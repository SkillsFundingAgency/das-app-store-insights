using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Configuration;

namespace SFA.DAS.AppStoreInsights.ZendeskBridge.Functions
{
    public class ZendeskWebhookFunction
    {
        private readonly IAppStoreRepository _repo;
        private readonly IAppleStoreClient _appleClient;
        private readonly IGooglePlayClient _googleClient;
        private readonly IZendeskClient _zendeskClient;
        private readonly ILogger<ZendeskWebhookFunction> _logger;
        private readonly ApplicationConfiguration _appConfig;

        public ZendeskWebhookFunction(
            IAppStoreRepository repo,
            IAppleStoreClient appleClient,
            IGooglePlayClient googleClient,
            IZendeskClient zendeskClient,
            IOptions<ApplicationConfiguration> appConfig,
            ILogger<ZendeskWebhookFunction> logger)
        {
            _repo = repo;
            _appleClient = appleClient;
            _googleClient = googleClient;
            _zendeskClient = zendeskClient;
            _appConfig = appConfig.Value;
            _logger = logger;
        }

        [Function("CreateZendeskTickets")]
        public async Task RunCreateTickets(
            [TimerTrigger("0 0 */1 * * *", RunOnStartup = true)] TimerInfo timer,
            FunctionContext context)
        {
            _logger.LogInformation("Checking for unprocessed reviews...");

            var appId = await _repo.GetAppIdAsync("Apprentice App", CancellationToken.None);
            var reviews = await _repo.GetUnprocessedReviewsAsync(appId, CancellationToken.None);

            foreach (var review in reviews)
            {
                try
                {
                    var vendorName = review.VendorId == 1 ? "Apple" : "Google";
                    var ticket = new ZendeskTicket
                    {
                        Subject = $"Feedback from {vendorName} (Rating: {review.Rating})",
                        Comment = review.Comment ?? string.Empty,
                        RequesterName = review.ReviewerName ?? "Anonymous",

                        RatingApp = GetRatingTag(review.Rating),
                        FeedbackApp = review.Title ?? review.Comment?.Substring(0, Math.Min(100, review.Comment?.Length ?? 0)) ?? "No feedback provided",
                        FeedbackIdApp = review.ExternalId,
                        AppStore = vendorName == "Apple" ? "apple_app" : "google_app",
                        ResponseApp = "bespoke_response"
                    };

                    var ticketId = await _zendeskClient.CreateTicketAsync(ticket, CancellationToken.None);
                    await _repo.UpdateReviewZendeskTicketIdAsync(review.Id, ticketId, CancellationToken.None);
                    _logger.LogInformation("Created Zendesk ticket {TicketId} for review {ReviewId}", ticketId, review.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create ticket for review {ReviewId}", review.Id);
                }
            }
        }

        private string GetRatingTag(byte rating)
        {
            return rating switch
            {
                1 => "1_app",
                2 => "2_app",
                3 => "3_app",
                4 => "4_app",
                5 => "5_app",
                _ => "5_app"
            };
        }
    }
}