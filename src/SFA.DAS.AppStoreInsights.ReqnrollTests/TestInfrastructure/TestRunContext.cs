using Microsoft.Extensions.Options;
using Moq;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;

namespace SFA.DAS.AppStoreInsights.ReqnrollTests.TestInfrastructure;

public class TestRunContext
{
    public IAppStoreRepository Repository { get; set; } = new InMemoryAppStoreRepository();
    public Mock<IAppleStoreClient> AppleClientMock { get; } = new();
    public Mock<IGooglePlayClient> GoogleClientMock { get; } = new();
    public IOptions<ApplicationConfiguration>? AppConfig { get; set; }

    public void Reset()
    {
        Repository = new InMemoryAppStoreRepository();
        AppleClientMock.Reset();
        GoogleClientMock.Reset();
        AppConfig = null;
    }
}

public class InMemoryAppStoreRepository : IAppStoreRepository
{
    private readonly List<Review> _reviews = new();
    private readonly List<UsageMetric> _metrics = new();
    private readonly List<ResponseEntry> _responses = new();
    private int _nextReviewId = 1;

    public Task<int> GetAppIdAsync(string appName, CancellationToken ct) => Task.FromResult(1);

    public Task<bool> ReviewExistsAsync(byte vendorId, string externalId, CancellationToken ct)
        => Task.FromResult(_reviews.Any(r => r.VendorId == vendorId && r.ExternalId == externalId));

    public Task InsertReviewAsync(Review review, CancellationToken ct)
    {
        if (review.Id == 0) review.Id = _nextReviewId++;
        _reviews.Add(review);
        return Task.CompletedTask;
    }

    public Task InsertUsageMetricAsync(UsageMetric metric, CancellationToken ct)
    {
        _metrics.Add(metric);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Review>> GetUnprocessedNegativeReviewsAsync(int appId, CancellationToken ct)
        => Task.FromResult(_reviews.Where(r => r.AppId == appId && r.IsNegative && string.IsNullOrEmpty(r.ZendeskTicketId)));

    public Task UpdateReviewZendeskTicketIdAsync(long reviewId, string ticketId, CancellationToken ct)
    {
        var review = _reviews.First(r => r.Id == reviewId);
        review.ZendeskTicketId = ticketId;
        review.ProcessedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task<Review?> GetReviewByZendeskTicketIdAsync(string ticketId, CancellationToken ct)
        => Task.FromResult(_reviews.FirstOrDefault(r => r.ZendeskTicketId == ticketId));

    public Task InsertResponseAsync(long reviewId, string responseText, string responder, DateTime respondedAt, CancellationToken ct)
    {
        _responses.Add(new ResponseEntry { ReviewId = reviewId, ResponseText = responseText, Responder = responder, RespondedAt = respondedAt });
        return Task.CompletedTask;
    }

    // Helper methods for tests
    public Task<List<Review>> GetAllReviewsAsync()
        => Task.FromResult(_reviews.ToList());

    public Task<List<UsageMetric>> GetAllUsageMetricsAsync()
        => Task.FromResult(_metrics.ToList());

    public Task<List<ResponseEntry>> GetAllResponsesAsync()
        => Task.FromResult(_responses.ToList());
}

public class ResponseEntry
{
    public long ReviewId { get; set; }
    public string ResponseText { get; set; } = string.Empty;
    public string Responder { get; set; } = string.Empty;
    public DateTime RespondedAt { get; set; }
}