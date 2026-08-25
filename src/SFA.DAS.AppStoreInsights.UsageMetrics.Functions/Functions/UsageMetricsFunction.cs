using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Functions.UsageMetrics.Configuration;

namespace SFA.DAS.AppStoreInsights.Functions.UsageMetrics
{
    public class UsageMetricsFunction
    {
        private readonly IAppStoreRepository _repo;
        private readonly IAppleStoreClient _appleClient;
        private readonly IGooglePlayClient _googleClient;
        private readonly ILogger _logger;
        private readonly ApplicationConfiguration _appConfig;

        public UsageMetricsFunction(
            IAppStoreRepository repo,
            IAppleStoreClient appleClient,
            IGooglePlayClient googleClient,
            IOptions<ApplicationConfiguration> appConfig,
            ILoggerFactory loggerFactory)
        {
            _repo = repo;
            _appleClient = appleClient;
            _googleClient = googleClient;
            _appConfig = appConfig.Value;
            _logger = loggerFactory.CreateLogger<UsageMetricsFunction>();
        }

        [Function("FetchAppleUsageMetrics")]
        public async Task RunApple([TimerTrigger("0 0 3 * * *", RunOnStartup = true)] TimerInfo timer)
        {
            _logger.LogInformation("Starting Apple usage metrics fetch");
            try
            {
                var appId = await _repo.GetAppIdAsync("Apprentice App", CancellationToken.None);
                var endDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                var startDate = endDate.AddDays(-7);
                var metrics = await _appleClient.GetDailyMetricsAsync(_appConfig.AppleAppId, startDate, endDate, CancellationToken.None);

                foreach (var metric in metrics)
                {
                    var usageMetric = new UsageMetric
                    {
                        AppId = appId,
                        VendorId = 1,
                        MetricDate = metric.Date.ToDateTime(TimeOnly.MinValue),
                        Downloads = metric.Downloads,
                        ActiveUsers = metric.DailyActiveDevices,
                        RawDataJson = JsonSerializer.Serialize(metric)
                    };
                    await _repo.InsertUsageMetricAsync(usageMetric, CancellationToken.None);
                    _logger.LogInformation("Inserted Apple usage metric for {Date}", metric.Date);
                }
                _logger.LogInformation("Completed Apple usage metrics fetch. Inserted {Count} records", metrics.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Apple usage metrics");
                throw;
            }
        }

        [Function("FetchGoogleUsageMetrics")]
        public async Task RunGoogle([TimerTrigger("0 0 3 * * *", RunOnStartup = true)] TimerInfo timer)
        {
            _logger.LogInformation("Starting Google usage metrics fetch");
            try
            {
                var appId = await _repo.GetAppIdAsync("Apprentice App", CancellationToken.None);
                var endDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                var startDate = endDate.AddDays(-30);
                var metrics = await _googleClient.GetDailyMetricsAsync(_appConfig.GooglePackageName, startDate, endDate, CancellationToken.None);

                foreach (var metric in metrics)
                {
                    var usageMetric = new UsageMetric
                    {
                        AppId = appId,
                        VendorId = 2,
                        MetricDate = metric.Date.ToDateTime(TimeOnly.MinValue),
                        Downloads = metric.Downloads,
                        ActiveUsers = metric.DailyActiveUsers,
                        RawDataJson = JsonSerializer.Serialize(metric)
                    };
                    await _repo.InsertUsageMetricAsync(usageMetric, CancellationToken.None);
                    _logger.LogInformation("Inserted Google usage metric for {Date}", metric.Date);
                }
                _logger.LogInformation("Completed Google usage metrics fetch. Inserted {Count} records", metrics.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Google usage metrics");
                throw;
            }
        }
    }
}