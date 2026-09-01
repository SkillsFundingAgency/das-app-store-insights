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
}