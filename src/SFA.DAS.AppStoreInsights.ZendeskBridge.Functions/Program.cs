using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Extensions;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Configuration;
using SFA.DAS.AppStoreInsights.ZendeskBridge.Functions.Extensions;
using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.ZendeskBridge.Functions;

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
                services.AddHttpClient<IZendeskClient, ZendeskClient>();
            })
            .Build()
            .RunAsync();
    }
}