using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    /// <summary>
    /// Client for Google Play Developer API (reviews, replies, download/usage metrics).
    /// </summary>
    public interface IGooglePlayClient
    {
        /// <summary>
        /// Retrieves new or updated reviews for the specified app since a given date.
        /// </summary>
        /// <param name="appPackageName">Google Play package name (e.g., "uk.gov.apprentice")</param>
        /// <param name="sinceUtc">Only return reviews with timestamp >= this value</param>
        /// <param name="cancellationToken"></param>
        /// <returns>List of normalized reviews</returns>
        Task<List<GooglePlayReview>> GetReviewsSinceAsync(
            string appPackageName,
            DateTime sinceUtc,
            CancellationToken cancellationToken = default);

        Task PostResponseAsync(
            string appPackageName,
            string reviewId,
            string replyText,
            CancellationToken cancellationToken = default);
    }

    // ========== Models ==========

    /// <summary>
    /// Normalised Google Play review.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class GooglePlayReview
    {
        /// <summary>Google's unique review ID (e.g., "gp:AOqpTOE...")</summary>
        public string ReviewId { get; set; }

        /// <summary>User’s display name (may be null)</summary>
        public string ReviewerName { get; set; }

        /// <summary>Rating 1-5 (1 = lowest)</summary>
        public int Rating { get; set; }

        /// <summary>Review title (Google Play allows titles; if not present, use empty)</summary>
        public string Title { get; set; }

        /// <summary>Review comment text</summary>
        public string Comment { get; set; }

        /// <summary>When the review was originally written (UTC)</summary>
        public DateTime ReviewDateUtc { get; set; }

        /// <summary>Last update time (Google can edit reviews; we store original date)</summary>
        public DateTime LastModifiedUtc { get; set; }

        /// <summary>Device information (e.g., "Google Pixel 6, Android 13") – from Google’s "device" field</summary>
        public string DeviceInfo { get; set; }

        /// <summary>Developer reply object (null if no reply yet)</summary>
        public GooglePlayDeveloperReply DeveloperReply { get; set; }
    }

    /// <summary>
    /// Developer’s reply to a Google Play review.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class GooglePlayDeveloperReply
    {
        public string ReplyText { get; set; }
        public DateTime ReplyDateUtc { get; set; }
        public string ReplyId { get; set; }   // Google’s internal id for the reply
    }

}