using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Extensions;
using SFA.DAS.AppStoreInsights.Shared.Extensions;
using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.Feedback.Functions;

[ExcludeFromCodeCoverage]
public partial class Program
{
    public static async Task Main(string[] args)
    {
        await new HostBuilder()
            .AddAppStoreInsightsHost((context, services) =>
            {
                services
                    .AddOptions()
                    .Configure<ApplicationConfiguration>(context.Configuration.GetSection(nameof(ApplicationConfiguration)))
                    .AddApplicationRegistrations();
            })
            .Build()
            .RunAsync();
    }
}