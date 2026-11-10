using SFA.DAS.AppStoreInsights.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace SFA.DAS.AppStoreInsights.Shared.Clients
{
    public interface IZendeskClient
    {
        /// <summary>
        /// Creates a new Zendesk ticket for feedback.
        /// </summary>
        /// <returns>Zendesk ticket ID (numeric string)</returns>
        Task<string> CreateTicketAsync(ZendeskTicket ticket, CancellationToken cancellationToken = default);

        /// <summary>
        /// Posts a reply to an existing ticket (used by webhook or manual).
        /// </summary>
        Task PostReplyAsync(string ticketId, string comment, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves all comments for a given ticket.
        /// </summary>
        Task<List<ZendeskComment>> GetTicketCommentsAsync(string ticketId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates an existing ticket (e.g., to set custom field values like ResponseApp).
        /// </summary>
        Task UpdateTicketAsync(string ticketId, object payload, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a comment on a Zendesk ticket.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ZendeskComment
    {
        public string Id { get; set; }                 // Unique comment ID
        public string Body { get; set; }               // Comment text
        public string AuthorRole { get; set; }         // "end-user", "agent", "admin" or "unknown"
        public bool Public { get; set; }               // True if public, false if internal note
        public DateTime CreatedAt { get; set; }        // UTC timestamp
    }
}