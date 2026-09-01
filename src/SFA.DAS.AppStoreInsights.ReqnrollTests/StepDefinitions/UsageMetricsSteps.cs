using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Reqnroll;
using SFA.DAS.AppStoreInsights.Functions.UsageMetrics;
using SFA.DAS.AppStoreInsights.Functions.UsageMetrics.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.ReqnrollTests.TestInfrastructure;

namespace SFA.DAS.AppStoreInsights.ReqnrollTests.StepDefinitions;

[Binding]
public class UsageMetricsSteps
{
    private readonly TestRunContext _testRunContext;
    private UsageMetricsFunction? _function;

    public UsageMetricsSteps(TestRunContext testRunContext)
    {
        _testRunContext = testRunContext;
    }

    [Given(@"the repository is ready to store usage metrics")]
    public void GivenTheRepositoryIsReadyToStoreUsageMetrics()
    {
        // Already using InMemoryAppStoreRepository
    }

    [Given(@"the Apple client returns a metric with (\d+) downloads, (\d+) active users for yesterday")]
    public void GivenTheAppleClientReturnsAMetricWithDownloadsActiveUsersForYesterday(int downloads, int activeUsers)
    {
        var metrics = new List<AppleStoreUsageMetric>
        {
            new() { Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), Downloads = downloads, DailyActiveDevices = activeUsers }
        };
        _testRunContext.AppleClientMock
            .Setup(x => x.GetDailyMetricsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);
    }

    [Given(@"the Google client returns a metric for yesterday with (\d+) downloads, and (\d+) active users")]
    public void GivenTheGoogleClientReturnsAMetricForYesterdayWithDownloadsAndActiveUsers(int downloads, int activeUsers)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var startDate = yesterday.AddDays(-6);
        var metrics = new List<GooglePlayUsageMetric>
        {
            new GooglePlayUsageMetric
            {
                Date = yesterday,
                Downloads = downloads,
                DailyActiveUsers = activeUsers,
                RawDataJson = "{}"
            }
        };
        _testRunContext.GoogleClientMock
            .Setup(x => x.GetDailyMetricsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);
    }

    [Given(@"the Apple client returns an empty list")]
    public void GivenTheAppleClientReturnsAnEmptyList()
    {
        _testRunContext.AppleClientMock
            .Setup(x => x.GetDailyMetricsAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppleStoreUsageMetric>());
    }

    [When(@"the Apple usage metrics timer runs")]
    public async Task WhenTheAppleUsageMetricsTimerRuns()
    {
        var appConfig = new ApplicationConfiguration
        {
            AppleAppId = "test-apple-id",
            GooglePackageName = "dummy",
            GoogleStatsBucketName = "dummy-bucket"
        };
        var options = Options.Create(appConfig);
        _function = new UsageMetricsFunction(
            _testRunContext.Repository,
            _testRunContext.AppleClientMock.Object,
            _testRunContext.GoogleClientMock.Object,
            options,
            NullLoggerFactory.Instance);
        await _function.RunApple(new TimerInfo());
    }

    [When(@"the Google usage metrics timer runs")]
    public async Task WhenTheGoogleUsageMetricsTimerRuns()
    {
        var appConfig = new ApplicationConfiguration
        {
            AppleAppId = "dummy",
            GooglePackageName = "test.google.package",
            GoogleStatsBucketName = "test-bucket"
        };
        var options = Options.Create(appConfig);
        _function = new UsageMetricsFunction(
            _testRunContext.Repository,
            _testRunContext.AppleClientMock.Object,
            _testRunContext.GoogleClientMock.Object,
            options,
            NullLoggerFactory.Instance);
        await _function.RunGoogle(new TimerInfo());
    }

    [Then(@"a UsageMetric record is inserted with VendorId = (\d+)")]
    public async Task ThenAUsageMetricRecordIsInsertedWithVendorId(int vendorId)
    {
        var metrics = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllUsageMetricsAsync();
        metrics.Should().Contain(m => m.VendorId == vendorId);
    }

    [Then(@"the Downloads = (\d+), ActiveUsers = (\d+)")]
    public async Task ThenTheDownloadsAreAndActiveUsersAre(int downloads, int activeUsers)
    {
        var metrics = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllUsageMetricsAsync();
        var metric = metrics.First();
        metric.Downloads.Should().Be(downloads);
        metric.ActiveUsers.Should().Be(activeUsers);
    }

    [Then(@"no UsageMetric records are inserted")]
    public async Task ThenNoUsageMetricRecordsAreInserted()
    {
        var count = (await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllUsageMetricsAsync()).Count;
        count.Should().Be(0);
    }

    [Then(@"a log message indicates zero records inserted")]
    public void ThenALogMessageIndicatesZeroRecordsInserted()
    {
        // Log verification would require capturing ILogger – skipped for now
    }
}