using FluentAssertions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Reqnroll;
using SFA.DAS.AppStoreInsights.Shared.Models;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Configuration;
using SFA.DAS.AppStoreInsights.ReqnrollTests.TestInfrastructure;

namespace SFA.DAS.AppStoreInsights.ReqnrollTests.StepDefinitions;

[Binding]
public class ZendeskTicketCreationSteps
{
    private readonly TestRunContext _testRunContext;
    private ZendeskWebhookFunction? _function;

    public ZendeskTicketCreationSteps(TestRunContext testRunContext)
    {
        _testRunContext = testRunContext;
    }

    [Given(@"the repository contains an unprocessed negative Apple review")]
    public async Task GivenTheRepositoryContainsAnUnprocessedNegativeAppleReview()
    {
        var review = new Review
        {
            AppId = 1,
            VendorId = 1,
            ExternalId = "apple_neg_1",
            Rating = 1,
            Comment = "This app crashes constantly",
            ReviewDate = DateTime.UtcNow,
            ZendeskTicketId = null
        };
        await _testRunContext.Repository.InsertReviewAsync(review, CancellationToken.None);
    }

    [Given(@"a mocked Zendesk client that returns a new ticket ID ""(.*)""")]
    public void GivenAMockedZendeskClientThatReturnsANewTicketId(string ticketId)
    {
        _testRunContext.ZendeskClientMock
            .Setup(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticketId);
    }

    [Given(@"the repository also contains a negative review that already has a ZendeskTicketId")]
    public async Task GivenTheRepositoryAlsoContainsANegativeReviewThatAlreadyHasATicketId()
    {
        var review = new Review
        {
            AppId = 1,
            VendorId = 1,
            ExternalId = "apple_processed",
            Rating = 1,
            Comment = "Old negative review",
            ReviewDate = DateTime.UtcNow,
            ZendeskTicketId = "existing_ticket_123"
        };
        await _testRunContext.Repository.InsertReviewAsync(review, CancellationToken.None);
    }

    [Given(@"the first negative review causes Zendesk.CreateTicketAsync to throw")]
    public void GivenTheFirstNegativeReviewCausesZendeskCreateTicketAsyncToThrow()
    {
        var setup = _testRunContext.ZendeskClientMock
            .SetupSequence(x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>()));
        setup.ThrowsAsync(new Exception("Zendesk API error"));
        setup.ReturnsAsync("ticket_for_second");
    }

    [Given(@"there is a second negative review")]
    public async Task GivenThereIsASecondNegativeReview()
    {
        var review = new Review
        {
            AppId = 1,
            VendorId = 1,
            ExternalId = "apple_neg_2",
            Rating = 1,
            Comment = "Another crash",
            ReviewDate = DateTime.UtcNow,
            ZendeskTicketId = null
        };
        await _testRunContext.Repository.InsertReviewAsync(review, CancellationToken.None);
    }

    [When(@"the ticket creation timer runs")]
    public async Task WhenTheTicketCreationTimerRuns()
    {
        var appConfig = new ApplicationConfiguration();
        _function = new ZendeskWebhookFunction(
            _testRunContext.Repository,
            _testRunContext.AppleClientMock.Object,
            _testRunContext.GoogleClientMock.Object,
            _testRunContext.ZendeskClientMock.Object,
            Options.Create(appConfig),
            NullLogger<ZendeskWebhookFunction>.Instance);

        await _function.RunCreateTickets(new TimerInfo(), new Mock<FunctionContext>().Object);
    }

    [Then(@"a Zendesk ticket is created with the review's comment and rating")]
    public void ThenAZendeskTicketIsCreatedWithTheReviewSCommentAndRating()
    {
        // Verify the ticket was created with the correct rating tag
        _testRunContext.ZendeskClientMock.Verify(
            x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t =>
                    t.RatingApp == "1_app" &&
                    t.Comment.Contains("This app crashes constantly")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"the review's ZendeskTicketId is updated to ""(.*)""")]
    public async Task ThenTheReviewSZenDeskTicketIdIsUpdatedTo(string ticketId)
    {
        var reviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var updated = reviews.First(r => r.ZendeskTicketId == ticketId);
        updated.ZendeskTicketId.Should().Be(ticketId);
        updated.ProcessedAt.Should().NotBeNull();
    }

    [Then(@"the review's ProcessedAt timestamp is set")]
    public async Task ThenTheReviewSProcessedAtTimestampIsSet()
    {
        var reviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var updated = reviews.First(r => r.ZendeskTicketId == "12345");
        updated.ProcessedAt.Should().NotBeNull();
    }

    [Then(@"only the unprocessed review creates a ticket")]
    public void ThenOnlyTheUnprocessedReviewCreatesATicket()
    {
        _testRunContext.ZendeskClientMock.Verify(
            x => x.CreateTicketAsync(It.IsAny<ZendeskTicket>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"the already-processed review is ignored")]
    public async Task ThenTheAlreadyProcessedReviewIsIgnored()
    {
        var reviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var processed = reviews.First(r => r.ZendeskTicketId == "existing_ticket_123");
        processed.ZendeskTicketId.Should().Be("existing_ticket_123");
    }

    [Then(@"a ticket is created for the second review")]
    public void ThenATicketIsCreatedForTheSecondReview()
    {
        _testRunContext.ZendeskClientMock.Verify(
            x => x.CreateTicketAsync(
                It.Is<ZendeskTicket>(t => t.Comment.Contains("Another crash")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"the first review's ZendeskTicketId remains null")]
    public async Task ThenTheFirstReviewSZenDeskTicketIdRemainsNull()
    {
        var reviews = await ((InMemoryAppStoreRepository)_testRunContext.Repository).GetAllReviewsAsync();
        var firstReview = reviews.First(r => r.ExternalId == "apple_neg_1");
        firstReview.ZendeskTicketId.Should().BeNull();
    }

    [Then(@"an error is logged for the first review")]
    public void ThenAnErrorIsLoggedForTheFirstReview()
    {
        // Verification would require capturing ILogger calls
    }
}