namespace SFA.DAS.AppStoreInsights.Shared.Models
{
    public class ZendeskTicket
    {
        public string Subject { get; set; }
        public string Comment { get; set; }
        public string RequesterName { get; set; }
        public string RequesterEmail { get; set; }
        public string Priority { get; set; } = "normal";
        public string[] Tags { get; set; } = { "app-store-feedback", "review" };

        // Custom fields for the Zendesk form
        public string RatingApp { get; set; }      // Rating (app) - dropdown
        public string FeedbackApp { get; set; }    // Feedback (app) - free text
        public string FeedbackIdApp { get; set; }  // Feedback ID (app) - free text
        public string AppStore { get; set; }       // App store (app) - dropdown
        public string ResponseApp { get; set; }    // Response (app) - dropdown
    }
}