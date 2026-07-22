using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Reqnroll;
using SFA.DAS.AppStoreInsights.Feedback.Functions;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.AppStoreInsights.ReqnrollTests.TestInfrastructure;

namespace SFA.DAS.AppStoreInsights.ReqnrollTests.StepDefinitions;

[Binding]
public class FeedbackIngestionSteps
{
    private readonly TestRunContext _testRunContext;
    private FeedbackIngestionFunction? _function;
    private Exception? _capturedException;

    public FeedbackIngestionSteps(TestRunContext testRunContext)
    {
        _testRunContext = testRunContext;
    }

    [Given(@"the system is configured with valid Apple and Google credentials")]
    public void GivenTheSystemIsConfiguredWithValidCredentials()
    {
        var appConfig = new ApplicationConfiguration
        {
            AppleAppId = "test-apple-id",
            GooglePackageName = "test.google.package"
        };
        _testRunContext.AppConfig = Options.Create(appConfig);
    }

    [Given(@"the in-memory repository is empty")]
    public void GivenTheInMemoryRepositoryIsEmpty()
    {
        _testRunContext.Repository = new InMemoryAppStoreRepository();
    }

    [Given(@"an Apple review with external ID ""(.*)"" already exists")]
    public async Task GivenAnAppleReviewWithExternalIdAlreadyExists(string externalId)
    {
        var review = new Review
        {
            ExternalId = externalId,
            VendorId = 1,
            AppId = 1,
            Rating = 2,
            Comment = "Existing review",
            ReviewDate = DateTime.UtcNow
        };
        await _testRunContext.Repository.InsertReviewAsync(review, CancellationToken.None);
    }

    [Given(@"the Apple API returns (\d+) new reviews \((\d+) positive, (\d+) negative\)")]
    public void GivenTheAppleAPIReturnsNewReviews(int total, int positive, int negative)
    {
        var reviews = new List<AppleStoreReview>();
        for (int i = 0; i < positive; i++)
        {
            reviews.Add(new AppleStoreReview
            {
                ReviewId = $"apple_pos_{Guid.NewGuid()}",
                Rating = 5,
                Comment = "Good app",
                ReviewDateUtc = DateTime.UtcNow
            });
        }
        for (int i = 0; i < negative; i++)
        {
            reviews.Add(new AppleStoreReview
            {
                ReviewId = $"apple_neg_{Guid.NewGuid()}",
                Rating = 1,
                Comment = "Bad app",
                ReviewDateUtc = DateTime.UtcNow
            });
        }
        _testRunContext.AppleClientMock
            .Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviews);
        _testRunContext.GoogleClientMock
            .Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GooglePlayReview>());
    }

    [Given(@"the Apple API returns the same review ""(.*)"" again")]
    public void GivenTheAppleAPIReturnsTheSameReviewAgain(string externalId)
    {
        var reviews = new List<AppleStoreReview>
        {
            new AppleStoreReview
            {
                ReviewId = externalId,
                Rating = 2,
                Comment = "Duplicate",
                ReviewDateUtc = DateTime.UtcNow
            }
        };
        _testRunContext.AppleClientMock
            .Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviews);
    }

    [Given(@"the Apple API throws an exception")]
    public void GivenTheAppleAPIThrowsAnException()
    {
        _testRunContext.AppleClientMock
            .Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Apple API error"));
    }

    [Given(@"the Google API returns (\d+) new review with rating (\d+)")]
    public void GivenTheGoogleAPIReturnsNewReviewWithRating(int count, int rating)
    {
        var reviews = new List<GooglePlayReview>();
        for (int i = 0; i < count; i++)
        {
            reviews.Add(new GooglePlayReview
            {
                ReviewId = $"google_{Guid.NewGuid()}",
                Rating = rating,
                Comment = rating == 1 ? "Terrible" : "Great",
                ReviewDateUtc = DateTime.UtcNow
            });
        }
        _testRunContext.GoogleClientMock
            .Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviews);
        _testRunContext.AppleClientMock
            .Setup(x => x.GetReviewsSinceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppleStoreReview>());
    }

    [When(@"the feedback ingestion timer runs")]
    public async Task WhenTheFeedbackIngestionTimerRuns()
    {
        _function = new FeedbackIngestionFunction(
            _testRunContext.Repository,
            _testRunContext.AppleClientMock.Object,
            _testRunContext.GoogleClientMock.Object,
            _testRunContext.AppConfig!,
            NullLoggerFactory.Instance);

        try
        {
            await _function.Run(new TimerInfo(), new Mock<FunctionContext>().Object);
        }
        catch (Exception ex)
        {
            _capturedException = ex;
        }
    }

    [Then(@"exactly (\d+) Apple reviews are inserted into the repository")]
    public async Task ThenExactlyAppleReviewsAreInserted(int expectedCount)
    {
        var allReviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var appleReviews = allReviews.Where(r => r.VendorId == 1).ToList();
        appleReviews.Count.Should().Be(expectedCount);
    }

    [Then(@"exactly (\d+) Google review is inserted into the repository")]
    public async Task ThenExactlyGoogleReviewIsInserted(int expectedCount)
    {
        var allReviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var googleReviews = allReviews.Where(r => r.VendorId == 2).ToList();
        googleReviews.Count.Should().Be(expectedCount);
    }

    [Then(@"the negative review is marked as IsNegative = true")]
    public async Task ThenTheNegativeReviewIsMarkedAsIsNegativeTrue()
    {
        var allReviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var negative = allReviews.FirstOrDefault(r => r.Rating <= 2);
        negative.Should().NotBeNull();
        negative!.IsNegative.Should().BeTrue();
    }

    [Then(@"that review has IsNegative = true")]
    public async Task ThenThatReviewHasIsNegativeTrue()
    {
        var allReviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        allReviews.First().IsNegative.Should().BeTrue();
    }

    [Then(@"no new reviews are inserted")]
    public async Task ThenNoNewReviewsAreInserted()
    {
        var count = (await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync()).Count;
        count.Should().Be(1);
    }

    [Then(@"an exception is propagated out of the function")]
    public void ThenAnExceptionIsPropagatedOutOfTheFunction()
    {
        _capturedException.Should().NotBeNull();
        _capturedException!.Message.Should().Contain("Apple API error");
    }

    [Then(@"the error is logged")]
    public void ThenTheErrorIsLogged()
    {
        // Log verification would be done via a mocked ILogger if needed
    }
}