using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Extensions;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Infrastructure;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using System.Diagnostics.CodeAnalysis;

namespace SFA.DAS.AppStoreInsights.Feedback.Functions;

[ExcludeFromCodeCoverage]
public partial class Program
{
    public static async Task Main(string[] args)
    {
        var host = new HostBuilder()
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddConfiguration();
            })
            .ConfigureServices((context, s) =>
            {
                s
                    .AddOptions()
                    .Configure<ApplicationConfiguration>(context.Configuration.GetSection(nameof(ApplicationConfiguration)))
                    .AddApplicationRegistrations();

                // Add Managed Identity support
                s.AddSingleton<IManagedIdentityTokenProvider, ManagedIdentityTokenProvider>();
                s.AddSingleton<IConnectionFactory, SqlServerConnectionFactory>();

                // Register the repository with the connection string from configuration
                s.AddSingleton<IAppStoreRepository>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var connectionString = config["SqlConnectionString"]
                        ?? throw new InvalidOperationException("SqlConnectionString not found");
                    var connectionFactory = sp.GetRequiredService<IConnectionFactory>();
                    return new SqlAppStoreRepository(connectionFactory, connectionString);
                });

                s.AddHttpClient<IAppleStoreClient, AppleStoreClient>();
                s.AddSingleton<IGooglePlayClient, GooglePlayClient>();
            })
            .Build();

        await host.RunAsync();
    }
}