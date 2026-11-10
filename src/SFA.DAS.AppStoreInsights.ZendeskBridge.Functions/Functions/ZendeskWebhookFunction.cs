using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Configuration;
using System.Diagnostics.CodeAnalysis;

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
        private readonly IConfiguration _configuration;

        public ZendeskWebhookFunction(
            IAppStoreRepository repo,
            IAppleStoreClient appleClient,
            IGooglePlayClient googleClient,
            IZendeskClient zendeskClient,
            IOptions<ApplicationConfiguration> appConfig,
            ILogger<ZendeskWebhookFunction> logger,
            IConfiguration configuration
            )
        {
            _repo = repo;
            _appleClient = appleClient;
            _googleClient = googleClient;
            _zendeskClient = zendeskClient;
            _appConfig = appConfig.Value;
            _logger = logger;
            _configuration = configuration;
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

        [Function("SyncZendeskReplies")]
        public async Task RunSyncReplies(
            [TimerTrigger("0 */30 * * * *", RunOnStartup = true)] TimerInfo timer)
        {
            _logger.LogInformation("Polling Zendesk for new replies on our tickets...");

            var appId = await _repo.GetAppIdAsync("Apprentice App", CancellationToken.None);
            var reviews = await _repo.GetReviewsWithTicketButNoResponseAsync(appId, CancellationToken.None);

            // Use the Zendesk tenant (sandbox vs production) as the single source of
            // truth for whether we're allowed to post replies back to the app stores.
            var isProduction = !IsSandboxZendesk();

            foreach (var review in reviews)
            {
                try
                {
                    var comments = await _zendeskClient.GetTicketCommentsAsync(review.ZendeskTicketId!, CancellationToken.None);

                    var agentReply = comments
                        .Where(c => c.Public && IsStaffRole(c.AuthorRole))
                        .OrderByDescending(c => c.CreatedAt)
                        .FirstOrDefault();

                    if (agentReply == null)
                        continue;

                    if (await _repo.ResponseExistsForReviewAsync(review.Id, agentReply.Id, CancellationToken.None))
                    {
                        _logger.LogDebug("Reply {CommentId} already processed for review {ReviewId}", agentReply.Id, review.Id);
                        continue;
                    }

                    if (review.VendorId != 1 && review.VendorId != 2)
                    {
                        _logger.LogWarning("Unknown vendor ID {VendorId} for review {ReviewId}", review.VendorId, review.Id);
                        continue;
                    }

                    if (isProduction)
                    {
                        if (review.VendorId == 1)
                        {
                            await _appleClient.PostResponseAsync(_appConfig.AppleAppId, review.ExternalId, agentReply.Body);
                        }
                        else if (review.VendorId == 2)
                        {
                            await _googleClient.PostResponseAsync(_appConfig.GooglePackageName, review.ExternalId, agentReply.Body);
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Skipping post to app store for review {ReviewId} - using sandbox Zendesk tenant. Reply that would have been posted: {Reply}",
                            review.Id,
                            agentReply.Body);
                    }

                    await UpdateZendeskResponseField(review.ZendeskTicketId!, agentReply.Body);

                    await _repo.InsertResponseAsync(
                        review.Id,
                        agentReply.Body,
                        "SupportAgent",
                        agentReply.CreatedAt,
                        CancellationToken.None,
                        agentReply.Id);

                    _logger.LogInformation("Synced reply from Zendesk ticket {TicketId} for review {ReviewId} (comment {CommentId})",
                        review.ZendeskTicketId, review.Id, agentReply.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync reply for review {ReviewId}", review.Id);
                }
            }
        }

        // Zendesk gives admins the role "admin" rather than "agent"; both are staff replies.
        private static bool IsStaffRole(string role) =>
            string.Equals(role, "agent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);

        private async Task UpdateZendeskResponseField(string ticketId, string responseText)
        {
            try
            {
                var clientId = _configuration["Zendesk:ClientId"];
                var fieldIds = ZendeskFieldIds.GetFieldIdsForClientId(clientId);

                if (fieldIds.ResponseApp.HasValue)
                {
                    var payload = new
                    {
                        ticket = new
                        {
                            custom_fields = new[]
                            {
                                new { id = fieldIds.ResponseApp.Value, value = responseText }
                            }
                        }
                    };

                    await _zendeskClient.UpdateTicketAsync(ticketId, payload, CancellationToken.None);
                    _logger.LogInformation("Updated Response (app) field for ticket {TicketId} with: {Response}",
                        ticketId, responseText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update Response (app) field for ticket {TicketId}", ticketId);
            }
        }

        [ExcludeFromCodeCoverage]
        private bool IsSandboxZendesk()
        {
            var clientId = _configuration["Zendesk:ClientId"];
            return !string.IsNullOrEmpty(clientId)
                && clientId.Contains("sandbox", StringComparison.OrdinalIgnoreCase);
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