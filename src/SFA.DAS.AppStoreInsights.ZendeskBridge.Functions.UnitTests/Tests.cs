using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Configuration;
using FluentAssertions;

namespace SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.UnitTests
{
    [TestFixture]
    public class ZendeskWebhookFunctionTests
    {
        private Mock<IAppStoreRepository> _repoMock;
        private Mock<IAppleStoreClient> _appleClientMock;
        private Mock<IGooglePlayClient> _googleClientMock;
        private Mock<IZendeskClient> _zendeskClientMock;
        private IOptions<ApplicationConfiguration> _appConfig;
        private Mock<ILogger<ZendeskWebhookFunction>> _loggerMock;
        private ZendeskWebhookFunction _function;
        private Mock<FunctionContext> _functionContextMock;
        private IConfiguration _configuration;

        [SetUp]
        public void SetUp()
        {
            _repoMock = new Mock<IAppStoreRepository>();
            _appleClientMock = new Mock<IAppleStoreClient>();
            _googleClientMock = new Mock<IGooglePlayClient>();
            _zendeskClientMock = new Mock<IZendeskClient>();
            _appConfig = Options.Create(new ApplicationConfiguration { AppleAppId = "apple123", GooglePackageName = "com.google" });
            _loggerMock = new Mock<ILogger<ZendeskWebhookFunction>>();

            // Default to a production-style client id so the sync tests exercise
            // the "post back to app stores" path. Tests that need the sandbox path
            // call RebuildFunctionWithClientId("...sandbox...") instead.
            RebuildFunctionWithClientId("prod-client-id");

            var serviceProvider = new Mock<IServiceProvider>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            _functionContextMock = new Mock<FunctionContext>();
            _functionContextMock.Setup(c => c.InstanceServices).Returns(serviceProvider.Object);
        }

        /// <summary>
        /// Rebuilds the function with a Zendesk client id. The client id is the single
        /// source of truth for both the field-id set and the sandbox/production branch
        /// inside ZendeskWebhookFunction, so tests that need a specific environment
        /// should call this rather than setting EnvironmentName.
        /// </summary>
        private void RebuildFunctionWithClientId(string clientId)
        {
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Zendesk:ClientId"] = clientId,
                    ["Zendesk:Subdomain"] = "das",
                    ["Zendesk:ClientSecret"] = "secret"
                })
                .Build();

            _function = new ZendeskWebhookFunction(
                _repoMock.Object,
                _appleClientMock.Object,
                _googleClientMock.Object,
                _zendeskClientMock.Object,
                _appConfig,
                _loggerMock.Object,
                _configuration);
        }

        [Test]
        public async Task RunCreateTickets_WhenUnprocessedReviewExists_CreatesTicketAndUpdatesReview()
        {
            var review = new Review { Id = 1, VendorId = 1, Rating = 3, Comment = "Okay app", ReviewerName = "User", ExternalId = "ext1" };
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetUnprocessedReviewsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>())).ReturnsAsync("12345");

            await _function.RunCreateTickets(It.IsAny<TimerInfo>(), _functionContextMock.Object);

            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.RatingApp == "3_app"),
                It.IsAny<CancellationToken>()), Times.Once);

            _repoMock.Verify(x => x.UpdateReviewZendeskTicketIdAsync(1, "12345", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunCreateTickets_WhenAppleReview_CreatesTicketWithAppleAppStoreTag()
        {
            var review = new Review { Id = 1, VendorId = 1, Rating = 5, Comment = "Great app", ReviewerName = "User", ExternalId = "ext1" };
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetUnprocessedReviewsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>())).ReturnsAsync("12345");

            await _function.RunCreateTickets(It.IsAny<TimerInfo>(), _functionContextMock.Object);

            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.AppStore == "apple_app"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunCreateTickets_WhenGoogleReview_CreatesTicketWithGoogleAppStoreTag()
        {
            var review = new Review { Id = 2, VendorId = 2, Rating = 4, Comment = "Good app", ReviewerName = "User", ExternalId = "ext2" };
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetUnprocessedReviewsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>())).ReturnsAsync("12345");

            await _function.RunCreateTickets(It.IsAny<TimerInfo>(), _functionContextMock.Object);

            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.AppStore == "google_app"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunCreateTickets_WhenApiThrows_LogsErrorAndContinues()
        {
            var review1 = new Review { Id = 1, VendorId = 1, Rating = 4, Comment = "First", ReviewerName = "U1", ExternalId = "ext1" };
            var review2 = new Review { Id = 2, VendorId = 1, Rating = 4, Comment = "Second", ReviewerName = "U2", ExternalId = "ext2" };
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetUnprocessedReviewsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { review1, review2 });
            _zendeskClientMock.SetupSequence(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Zendesk error"))
                .ReturnsAsync("67890");

            await _function.RunCreateTickets(It.IsAny<TimerInfo>(), _functionContextMock.Object);

            _repoMock.Verify(x => x.UpdateReviewZendeskTicketIdAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _repoMock.Verify(x => x.UpdateReviewZendeskTicketIdAsync(2, "67890", It.IsAny<CancellationToken>()), Times.Once);
            _loggerMock.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to create ticket for review 1")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [Test]
        public async Task RunCreateTickets_WithDifferentRatings_CreatesCorrectRatingTags()
        {
            var reviews = new[]
            {
                new Review { Id = 1, VendorId = 1, Rating = 1, Comment = "Terrible", ReviewerName = "U1", ExternalId = "ext1" },
                new Review { Id = 2, VendorId = 1, Rating = 2, Comment = "Bad", ReviewerName = "U2", ExternalId = "ext2" },
                new Review { Id = 3, VendorId = 1, Rating = 3, Comment = "Okay", ReviewerName = "U3", ExternalId = "ext3" },
                new Review { Id = 4, VendorId = 1, Rating = 4, Comment = "Good", ReviewerName = "U4", ExternalId = "ext4" },
                new Review { Id = 5, VendorId = 1, Rating = 5, Comment = "Great", ReviewerName = "U5", ExternalId = "ext5" }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetUnprocessedReviewsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(reviews);
            _zendeskClientMock.Setup(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>())).ReturnsAsync("12345");

            await _function.RunCreateTickets(It.IsAny<TimerInfo>(), _functionContextMock.Object);

            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.RatingApp == "1_app" && t.Comment.Contains("Terrible")),
                It.IsAny<CancellationToken>()), Times.Once);
            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.RatingApp == "2_app" && t.Comment.Contains("Bad")),
                It.IsAny<CancellationToken>()), Times.Once);
            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.RatingApp == "3_app" && t.Comment.Contains("Okay")),
                It.IsAny<CancellationToken>()), Times.Once);
            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.RatingApp == "4_app" && t.Comment.Contains("Good")),
                It.IsAny<CancellationToken>()), Times.Once);
            _zendeskClientMock.Verify(x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.RatingApp == "5_app" && t.Comment.Contains("Great")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunSyncReplies_WhenAgentReplyExists_PostsReplyToAppleAndSavesResponse()
        {
            var review = new Review
            {
                Id = 1,
                VendorId = 1,
                ExternalId = "apple_review_1",
                ZendeskTicketId = "12345"
            };

            var comments = new List<ZendeskComment>
            {
                new ZendeskComment
                {
                    Id = "comment_1",
                    Body = "Thank you for your feedback!",
                    AuthorRole = "agent",
                    Public = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(comments);
            _repoMock.Setup(x => x.ResponseExistsForReviewAsync(1, "comment_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await _function.RunSyncReplies(new TimerInfo());

            _appleClientMock.Verify(x => x.PostResponseAsync("apple123", "apple_review_1", "Thank you for your feedback!", It.IsAny<CancellationToken>()), Times.Once);
            _repoMock.Verify(x => x.InsertResponseAsync(1, "Thank you for your feedback!", "SupportAgent", It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), "comment_1"), Times.Once);
            _zendeskClientMock.Verify(x => x.UpdateTicketAsync("12345", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunSyncReplies_WhenAdminReplyExists_PostsReplyAndSavesResponse()
        {
            var review = new Review
            {
                Id = 1,
                VendorId = 1,
                ExternalId = "apple_review_1",
                ZendeskTicketId = "12345"
            };

            var comments = new List<ZendeskComment>
            {
                new ZendeskComment
                {
                    Id = "comment_1",
                    Body = "Thanks, we're looking into this.",
                    AuthorRole = "admin",
                    Public = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(comments);
            _repoMock.Setup(x => x.ResponseExistsForReviewAsync(1, "comment_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await _function.RunSyncReplies(new TimerInfo());

            _appleClientMock.Verify(x => x.PostResponseAsync("apple123", "apple_review_1", "Thanks, we're looking into this.", It.IsAny<CancellationToken>()), Times.Once);
            _repoMock.Verify(x => x.InsertResponseAsync(1, "Thanks, we're looking into this.", "SupportAgent", It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), "comment_1"), Times.Once);
        }

        [Test]
        public async Task RunSyncReplies_WhenGoogleReview_ShouldPostToGoogle()
        {
            var review = new Review
            {
                Id = 2,
                VendorId = 2,
                ExternalId = "google_review_1",
                ZendeskTicketId = "67890"
            };

            var comments = new List<ZendeskComment>
            {
                new ZendeskComment
                {
                    Id = "comment_2",
                    Body = "Thanks for your review!",
                    AuthorRole = "agent",
                    Public = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("67890", It.IsAny<CancellationToken>()))
                .ReturnsAsync(comments);
            _repoMock.Setup(x => x.ResponseExistsForReviewAsync(2, "comment_2", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await _function.RunSyncReplies(new TimerInfo());

            _googleClientMock.Verify(x => x.PostResponseAsync("com.google", "google_review_1", "Thanks for your review!", It.IsAny<CancellationToken>()), Times.Once);
            _repoMock.Verify(x => x.InsertResponseAsync(2, "Thanks for your review!", "SupportAgent", It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), "comment_2"), Times.Once);
        }

        [Test]
        public async Task RunSyncReplies_WhenNonProduction_SkipsPostToAppStoreButStillRecordsResponse()
        {
            // Sandbox Zendesk tenant -> the function must not post back to the app stores.
            RebuildFunctionWithClientId("test-sandbox-client");

            var review = new Review
            {
                Id = 1,
                VendorId = 1,
                ExternalId = "apple_review_1",
                ZendeskTicketId = "12345"
            };

            var comments = new List<ZendeskComment>
            {
                new ZendeskComment
                {
                    Id = "comment_1",
                    Body = "Thank you for your feedback!",
                    AuthorRole = "agent",
                    Public = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(comments);
            _repoMock.Setup(x => x.ResponseExistsForReviewAsync(1, "comment_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            await _function.RunSyncReplies(new TimerInfo());

            _appleClientMock.Verify(x => x.PostResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _googleClientMock.Verify(x => x.PostResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _repoMock.Verify(x => x.InsertResponseAsync(1, "Thank you for your feedback!", "SupportAgent", It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), "comment_1"), Times.Once);
            _zendeskClientMock.Verify(x => x.UpdateTicketAsync("12345", It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunSyncReplies_WhenReplyAlreadyProcessed_SkipsIt()
        {
            var review = new Review
            {
                Id = 1,
                VendorId = 1,
                ExternalId = "apple_review_1",
                ZendeskTicketId = "12345"
            };

            var comments = new List<ZendeskComment>
            {
                new ZendeskComment
                {
                    Id = "comment_1",
                    Body = "Thank you for your feedback!",
                    AuthorRole = "agent",
                    Public = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(comments);
            _repoMock.Setup(x => x.ResponseExistsForReviewAsync(1, "comment_1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            await _function.RunSyncReplies(new TimerInfo());

            _appleClientMock.Verify(x => x.PostResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _repoMock.Verify(x => x.InsertResponseAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task RunSyncReplies_WhenNoAgentReply_DoesNothing()
        {
            var review = new Review
            {
                Id = 1,
                VendorId = 1,
                ExternalId = "apple_review_1",
                ZendeskTicketId = "12345"
            };

            var comments = new List<ZendeskComment>
            {
                new ZendeskComment
                {
                    Id = "comment_1",
                    Body = "User said something",
                    AuthorRole = "end-user",
                    Public = true,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("12345", It.IsAny<CancellationToken>()))
                .ReturnsAsync(comments);

            await _function.RunSyncReplies(new TimerInfo());

            _appleClientMock.Verify(x => x.PostResponseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _repoMock.Verify(x => x.InsertResponseAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>(), It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task RunSyncReplies_WhenExceptionOccurs_LogsErrorAndContinues()
        {
            var review = new Review
            {
                Id = 1,
                VendorId = 1,
                ExternalId = "apple_review_1",
                ZendeskTicketId = "12345"
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _repoMock.Setup(x => x.GetReviewsWithTicketButNoResponseAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { review });
            _zendeskClientMock.Setup(x => x.GetTicketCommentsAsync("12345", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Zendesk API error"));

            await _function.RunSyncReplies(new TimerInfo());

            _loggerMock.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to sync reply for review 1")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}