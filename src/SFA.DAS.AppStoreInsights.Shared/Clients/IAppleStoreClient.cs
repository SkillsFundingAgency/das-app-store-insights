using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    /// <summary>
    /// Client for Apple App Store Connect API (reviews, replies, sales & download metrics).
    /// </summary>
    public interface IAppleStoreClient
    {
        /// <summary>
        /// Retrieves new or updated customer reviews for the specified app since a given date.
        /// Apple’s Reviews endpoint can be filtered by lastModified date.
        /// </summary>
        /// <param name="appAppleId">Apple numeric app ID (e.g., "1234567890")</param>
        /// <param name="sinceUtc">Only return reviews modified on or after this timestamp</param>
        /// <param name="cancellationToken"></param>
        /// <returns>List of normalized reviews</returns>
        Task<List<AppleStoreReview>> GetReviewsSinceAsync(
            string appAppleId,
            DateTime sinceUtc,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves daily download, install, and usage metrics (Sales & Trends reports).
        /// Apple typically refreshes metrics once per day (by 6 AM UTC).
        /// </summary>
        /// <param name="appAppleId"></param>
        /// <param name="startDate">Inclusive</param>
        /// <param name="endDate">Inclusive</param>
        /// <param name="cancellationToken"></param>
        /// <returns>List of daily metrics</returns>
        Task<List<AppleStoreUsageMetric>> GetDailyMetricsAsync(
            string appAppleId,
            DateOnly startDate,
            DateOnly endDate,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Posts a developer response to an existing customer review.
        /// Only available for ratings and reviews in the App Store.
        /// </summary>
        /// <param name="appAppleId"></param>
        /// <param name="reviewId">Apple’s unique review identifier (UUID style)</param>
        /// <param name="responseText">The developer reply (max 1,000 characters)</param>
        /// <param name="cancellationToken"></param>
        Task PostResponseAsync(
            string appAppleId,
            string reviewId,
            string responseText,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests outbound internet connectivity to https://www.google.com.
        /// This method is called automatically on client initialization.
        /// </summary>
        Task<bool> TestConnectivityAsync();
    }

    // ========== Models ==========

    /// <summary>
    /// Normalised Apple App Store review.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class AppleStoreReview
    {
        /// <summary>Apple’s unique review ID (e.g., "A1B2C3D4-...")</summary>
        public string ReviewId { get; set; }

        /// <summary>User’s display name (may be "Anonymous" or nil)</summary>
        public string ReviewerName { get; set; }

        /// <summary>Rating 1-5 (1 = worst)</summary>
        public int Rating { get; set; }

        /// <summary>Review title – Apple does not have a separate title; set to empty string</summary>
        public string Title { get; set; }

        /// <summary>Review comment text</summary>
        public string Comment { get; set; }

        /// <summary>When the review was originally written (UTC)</summary>
        public DateTime ReviewDateUtc { get; set; }

        /// <summary>Last time the review was modified (user edit or developer reply) (UTC)</summary>
        public DateTime LastModifiedUtc { get; set; }

        /// <summary>Device model and OS (e.g., "iPhone14,2, iOS 17.1")</summary>
        public string DeviceInfo { get; set; }

        /// <summary>Developer reply (if any)</summary>
        public AppleStoreDeveloperReply DeveloperReply { get; set; }
    }

    /// <summary>
    /// Developer’s response to an Apple review.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class AppleStoreDeveloperReply
    {
        public string ResponseText { get; set; }
        public DateTime ResponseDateUtc { get; set; }
        public string ResponseId { get; set; } // Apple’s internal ID for the response
    }

    /// <summary>
    /// Daily download, install, and session metrics from Apple Sales & Trends.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class AppleStoreUsageMetric
    {
        public DateOnly Date { get; set; }
        public int Downloads { get; set; }      // First‑time downloads (units)
        public int Installs { get; set; }       // Total installations (including redownloads)
        public int Uninstalls { get; set; }     // Not directly available; set to 0 or omit
        public int Sessions { get; set; }       // Number of app launches
        public int DailyActiveDevices { get; set; }
    }
}