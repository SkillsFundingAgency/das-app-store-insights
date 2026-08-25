using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using SFA.DAS.AppStoreInsights.Functions.UsageMetrics;
using SFA.DAS.AppStoreInsights.Functions.UsageMetrics.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using FluentAssertions;

namespace SFA.DAS.AppStoreInsights.Functions.UsageMetrics.UnitTests
{
    [TestFixture]
    public class UsageMetricsFunctionTests
    {
        private Mock<IAppStoreRepository> _repoMock;
        private Mock<IAppleStoreClient> _appleClientMock;
        private Mock<IGooglePlayClient> _googleClientMock;
        private Mock<ILoggerFactory> _loggerFactoryMock;
        private Mock<ILogger<UsageMetricsFunction>> _loggerMock;
        private IOptions<ApplicationConfiguration> _appConfig;
        private UsageMetricsFunction _function;

        [SetUp]
        public void SetUp()
        {
            _repoMock = new Mock<IAppStoreRepository>();
            _appleClientMock = new Mock<IAppleStoreClient>();
            _googleClientMock = new Mock<IGooglePlayClient>();
            _loggerFactoryMock = new Mock<ILoggerFactory>();
            _loggerMock = new Mock<ILogger<UsageMetricsFunction>>();
            _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(_loggerMock.Object);
            _appConfig = Options.Create(new ApplicationConfiguration
            {
                AppleAppId = "apple123",
                GooglePackageName = "com.google",
                GoogleStatsBucketName = "test-bucket"
            });
            _function = new UsageMetricsFunction(_repoMock.Object, _appleClientMock.Object, _googleClientMock.Object, _appConfig, _loggerFactoryMock.Object);
        }

        #region RunApple Tests

        [Test]
        public async Task RunApple_WhenSuccessful_FetchesAppIdAndMetricsAndInserts()
        {
            var expectedAppId = 123;
            var metrics = new List<AppleStoreUsageMetric>
            {
                new() { Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Downloads = 100, DailyActiveDevices = 80 }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(expectedAppId);
            _appleClientMock
                .Setup(x => x.GetDailyMetricsAsync("apple123", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(metrics);

            await _function.RunApple(It.IsAny<TimerInfo>());

            _repoMock.Verify(x => x.InsertUsageMetricAsync(
                It.Is<UsageMetric>(m => m.VendorId == 1 && m.Downloads == 100 && m.ActiveUsers == 80),
                It.IsAny<CancellationToken>()), Times.Once);
            _loggerMock.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Completed Apple usage metrics fetch")),
                null, It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        [Test]
        public async Task RunApple_WhenNoMetricsReturned_DoesNotInsert()
        {
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _appleClientMock
                .Setup(x => x.GetDailyMetricsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<AppleStoreUsageMetric>());

            await _function.RunApple(It.IsAny<TimerInfo>());

            _repoMock.Verify(x => x.InsertUsageMetricAsync(It.IsAny<UsageMetric>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void RunApple_WhenGetAppIdThrows_LogsErrorAndThrows()
        {
            var ex = new Exception("DB error");
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ThrowsAsync(ex);

            Assert.ThrowsAsync<Exception>(() => _function.RunApple(It.IsAny<TimerInfo>()));
            _loggerMock.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error fetching Apple usage metrics")),
                ex, It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion

        #region RunGoogle Tests

        [Test]
        public async Task RunGoogle_WhenSuccessful_FetchesAppIdAndMetricsAndInserts()
        {
            var expectedAppId = 123;
            var metrics = new List<GooglePlayUsageMetric>
            {
                new()
                {
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
                    Downloads = 200,
                    DailyActiveUsers = 150,
                    RawDataJson = "{}"
                }
            };

            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(expectedAppId);
            _googleClientMock
                .Setup(x => x.GetDailyMetricsAsync("com.google", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(metrics);

            await _function.RunGoogle(It.IsAny<TimerInfo>());

            _repoMock.Verify(x => x.InsertUsageMetricAsync(
                It.Is<UsageMetric>(m => m.VendorId == 2 && m.Downloads == 200 && m.ActiveUsers == 150),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RunGoogle_WhenNoMetricsReturned_DoesNotInsert()
        {
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ReturnsAsync(1);
            _googleClientMock
                .Setup(x => x.GetDailyMetricsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<GooglePlayUsageMetric>());

            await _function.RunGoogle(It.IsAny<TimerInfo>());

            _repoMock.Verify(x => x.InsertUsageMetricAsync(It.IsAny<UsageMetric>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void RunGoogle_WhenGetAppIdThrows_LogsErrorAndThrows()
        {
            var ex = new Exception("DB error");
            _repoMock.Setup(x => x.GetAppIdAsync("Apprentice App", It.IsAny<CancellationToken>())).ThrowsAsync(ex);

            Assert.ThrowsAsync<Exception>(() => _function.RunGoogle(It.IsAny<TimerInfo>()));
            _loggerMock.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error fetching Google usage metrics")),
                ex, It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        #endregion
    }
}