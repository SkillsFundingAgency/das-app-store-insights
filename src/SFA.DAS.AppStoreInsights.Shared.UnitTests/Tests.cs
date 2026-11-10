using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.UnitTests
{
    #region Model Tests
    [TestFixture]
    public class ReviewModelTests
    {
        [Test]
        public void IsNegative_WithRatingOne_ReturnsTrue() => new Review { Rating = 1 }.IsNegative.Should().BeTrue();
        [Test]
        public void IsNegative_WithRatingTwo_ReturnsTrue() => new Review { Rating = 2 }.IsNegative.Should().BeTrue();
        [Test]
        public void IsNegative_WithRatingThree_ReturnsFalse() => new Review { Rating = 3 }.IsNegative.Should().BeFalse();
        [Test]
        public void IsNegative_WithRatingFour_ReturnsFalse() => new Review { Rating = 4 }.IsNegative.Should().BeFalse();
        [Test]
        public void IsNegative_WithRatingFive_ReturnsFalse() => new Review { Rating = 5 }.IsNegative.Should().BeFalse();
    }

    [TestFixture]
    public class UsageMetricModelTests
    {
        [Test]
        public void UsageMetric_CanSetProperties()
        {
            var metric = new UsageMetric
            {
                AppId = 1,
                VendorId = 1,
                MetricDate = DateTime.UtcNow,
                Downloads = 100,
                ActiveUsers = 80,
                RawDataJson = "{}"
            };
            metric.AppId.Should().Be(1);
            metric.Downloads.Should().Be(100);
            metric.ActiveUsers.Should().Be(80);
        }
    }
    #endregion

    #region Zendesk Comment Parser Tests
    [TestFixture]
    public class ZendeskCommentParserTests
    {
        private const string PageWithUsers = @"{
            ""comments"": [
                { ""id"": 1, ""author_id"": 100, ""body"": ""Original feedback"", ""public"": true, ""created_at"": ""2026-10-07T08:11:23Z"" },
                { ""id"": 2, ""author_id"": 200, ""body"": ""Agent reply"", ""public"": true, ""created_at"": ""2026-10-08T09:00:00Z"" },
                { ""id"": 3, ""author_id"": 300, ""body"": ""Admin reply"", ""public"": true, ""created_at"": ""2026-10-09T09:00:00Z"" },
                { ""id"": 4, ""author_id"": 200, ""body"": ""Internal note"", ""public"": false, ""created_at"": ""2026-10-09T10:00:00Z"" }
            ],
            ""users"": [
                { ""id"": 100, ""role"": ""end-user"" },
                { ""id"": 200, ""role"": ""agent"" },
                { ""id"": 300, ""role"": ""admin"" }
            ],
            ""next_page"": null,
            ""previous_page"": null,
            ""count"": 4
        }";

        [Test]
        public void ParsePage_ResolvesAuthorRoleFromSideloadedUsers()
        {
            var comments = new System.Collections.Generic.List<ZendeskComment>();

            ZendeskCommentParser.ParsePage(PageWithUsers, comments);

            comments.Should().HaveCount(4);
            comments[0].AuthorRole.Should().Be("end-user");
            comments[1].AuthorRole.Should().Be("agent");
            comments[2].AuthorRole.Should().Be("admin");
            comments[3].AuthorRole.Should().Be("agent");
        }

        [Test]
        public void ParsePage_MapsCommentFields()
        {
            var comments = new System.Collections.Generic.List<ZendeskComment>();

            ZendeskCommentParser.ParsePage(PageWithUsers, comments);

            comments[1].Id.Should().Be("2");
            comments[1].Body.Should().Be("Agent reply");
            comments[1].Public.Should().BeTrue();
            comments[1].CreatedAt.ToUniversalTime().Should().Be(new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc));
            comments[3].Public.Should().BeFalse();
        }

        [Test]
        public void ParsePage_WithoutSideloadedUsers_ReturnsUnknownRole()
        {
            // This is the shape Zendesk returns without ?include=users: author_id only, no author object.
            const string json = @"{ ""comments"": [ { ""id"": 1, ""author_id"": 200, ""body"": ""Reply"", ""public"": true } ], ""next_page"": null }";
            var comments = new System.Collections.Generic.List<ZendeskComment>();

            ZendeskCommentParser.ParsePage(json, comments);

            comments.Should().ContainSingle().Which.AuthorRole.Should().Be(ZendeskCommentParser.UnknownRole);
        }

        [Test]
        public void ParsePage_ReturnsNextPageUrl()
        {
            const string json = @"{ ""comments"": [], ""users"": [], ""next_page"": ""https://example.zendesk.com/api/v2/tickets/1/comments.json?include=users&page=2"" }";

            var nextPage = ZendeskCommentParser.ParsePage(json, new System.Collections.Generic.List<ZendeskComment>());

            nextPage.Should().Be("https://example.zendesk.com/api/v2/tickets/1/comments.json?include=users&page=2");
        }

        [Test]
        public void ParsePage_WhenNoNextPage_ReturnsNull()
        {
            var nextPage = ZendeskCommentParser.ParsePage(PageWithUsers, new System.Collections.Generic.List<ZendeskComment>());

            nextPage.Should().BeNull();
        }
    }
    #endregion
}