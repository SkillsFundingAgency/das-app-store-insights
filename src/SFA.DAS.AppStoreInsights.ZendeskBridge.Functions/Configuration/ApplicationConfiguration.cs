using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Configuration;

[ExcludeFromCodeCoverage]
public class ApplicationConfiguration
{
    public string AppleAppId { get; set; }
    public string GooglePackageName { get; set; }
}