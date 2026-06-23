using Microsoft.Azure.Functions.Worker;

namespace SFA.DAS.AppStoreInsights.Feedback.Functions
{
    public class FeedbackIngestionFunction
    {
        public FeedbackIngestionFunction()
        {
        }

        [Function("FetchAppStoreFeedback")]
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer, FunctionContext context)
        {
            
        }
    }
}