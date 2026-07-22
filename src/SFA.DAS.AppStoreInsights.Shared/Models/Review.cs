using System;

namespace SFA.DAS.AppStoreInsights.Shared.Models
{
    public class Review
    {
        public long Id { get; set; }
        public int AppId { get; set; }
        public byte VendorId { get; set; }
        public string ExternalId { get; set; } = string.Empty;
        public string ReviewerName { get; set; } = string.Empty;
        public byte Rating { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public DateTime ReviewDate { get; set; }
        public string DeviceInfo { get; set; } = string.Empty;
        public bool IsNegative => Rating <= 2;
        public string? ZendeskTicketId { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}