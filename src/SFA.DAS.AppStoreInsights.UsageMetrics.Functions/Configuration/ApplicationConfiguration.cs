using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.Functions.UsageMetrics.Configuration;

[ExcludeFromCodeCoverage]
public class ApplicationConfiguration
{
    public string AppleAppId { get; set; }          // e.g., "1234567890"
    public string GooglePackageName { get; set; }   // e.g., "uk.gov.apprentice"
    public string GoogleStatsBucketName { get; set; }

}