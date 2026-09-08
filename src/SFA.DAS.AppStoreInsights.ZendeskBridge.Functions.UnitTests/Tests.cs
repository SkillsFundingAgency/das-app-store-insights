using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
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

        [SetUp]
        public void SetUp()
        {
            _repoMock = new Mock<IAppStoreRepository>();
            _appleClientMock = new Mock<IAppleStoreClient>();
            _googleClientMock = new Mock<IGooglePlayClient>();
            _zendeskClientMock = new Mock<IZendeskClient>();
            _appConfig = Options.Create(new ApplicationConfiguration { AppleAppId = "apple123", GooglePackageName = "com.google" });
            _loggerMock = new Mock<ILogger<ZendeskWebhookFunction>>();
            _function = new ZendeskWebhookFunction(_repoMock.Object, _appleClientMock.Object, _googleClientMock.Object, _zendeskClientMock.Object, _appConfig, _loggerMock.Object);

            var serviceProvider = new Mock<IServiceProvider>();
            var loggerFactory = new Mock<ILoggerFactory>();
            loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
            serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
            _functionContextMock = new Mock<FunctionContext>();
            _functionContextMock.Setup(c => c.InstanceServices).Returns(serviceProvider.Object);
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
    }
}