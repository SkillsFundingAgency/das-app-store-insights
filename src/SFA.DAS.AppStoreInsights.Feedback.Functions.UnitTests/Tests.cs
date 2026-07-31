using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using SFA.DAS.AppStoreInsights.Feedback.Functions;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using FluentAssertions;

namespace SFA.DAS.AppStoreInsights.Feedback.Functions.UnitTests
{
    [TestFixture]
    public class FeedbackIngestionFunctionTests
    {
        private Mock<IAppStoreRepository> _repoMock;
        private Mock<IAppleStoreClient> _appleClientMock;
        private Mock<IGooglePlayClient> _googleClientMock;
        private Mock<ILoggerFactory> _loggerFactoryMock;
        private Mock<ILogger<FeedbackIngestionFunction>> _loggerMock;
        private IOptions<ApplicationConfiguration> _appConfig;
        private FeedbackIngestionFunction _function;

        [SetUp]
        public void SetUp()
        {
            _repoMock = new Mock<IAppStoreRepository>();
            _appleClientMock = new Mock<IAppleStoreClient>();
            _googleClientMock = new Mock<IGooglePlayClient>();
            _loggerFactoryMock = new Mock<ILoggerFactory>();
            _loggerMock = new Mock<ILogger<FeedbackIngestionFunction>>();
            _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(_loggerMock.Object);
            _appConfig = Options.Create(new ApplicationConfiguration { AppleAppId = "apple123", GooglePackageName = "com.google" });
            _function = new FeedbackIngestionFunction(_repoMock.Object, _appleClientMock.Object, _googleClientMock.Object, _appConfig, _loggerFactoryMock.Object);
        }

        [Test]
        public async Task Run_WhenNewAppleReviewsExist_InsertsThem()
        {
            var appId = 1;
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(appId);
            _repoMock.Setup(x => x.ReviewExistsAsync(It.IsAny<byte>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var appleReviews = new List<AppleStoreReview>
            {
                new() { ReviewId = "apple1", Rating = 2, Comment = "Bad", ReviewerName = "User", ReviewDateUtc = DateTime.UtcNow }
            };
            _appleClientMock.Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(appleReviews);
            _googleClientMock.Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<GooglePlayReview>());

            await _function.Run(It.IsAny<TimerInfo>(), Mock.Of<FunctionContext>());

            _repoMock.Verify(x => x.InsertReviewAsync(It.Is<Review>(r => r.ExternalId == "apple1"), It.IsAny<CancellationToken>()), Times.Once);
            _loggerMock.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Inserted Apple review")), null, It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [Test]
        public async Task Run_WhenDuplicateReviewExists_SkipsInsert()
        {
            var appId = 1;
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(appId);
            _repoMock.Setup(x => x.ReviewExistsAsync(1, "dup", It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var appleReviews = new List<AppleStoreReview> { new() { ReviewId = "dup", Rating = 3, Comment = "Ok" } };
            _appleClientMock.Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(appleReviews);
            _googleClientMock.Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<GooglePlayReview>());

            await _function.Run(It.IsAny<TimerInfo>(), Mock.Of<FunctionContext>());

            _repoMock.Verify(x => x.InsertReviewAsync(It.IsAny<Review>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Run_WhenGoogleReviewsExist_InsertsGoogleReviews()
        {
            var appId = 1;
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(appId);
            _repoMock.Setup(x => x.ReviewExistsAsync(2, "google1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var googleReviews = new List<GooglePlayReview> { new() { ReviewId = "google1", Rating = 1, Comment = "Crash", ReviewerName = "User2", ReviewDateUtc = DateTime.UtcNow } };
            _appleClientMock.Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<AppleStoreReview>());
            _googleClientMock.Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleReviews);

            await _function.Run(It.IsAny<TimerInfo>(), Mock.Of<FunctionContext>());

            _repoMock.Verify(x => x.InsertReviewAsync(It.Is<Review>(r => r.ExternalId == "google1"), It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}