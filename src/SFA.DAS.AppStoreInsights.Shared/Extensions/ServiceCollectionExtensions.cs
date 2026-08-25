using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Infrastructure;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using SFA.DAS.Configuration.AzureTableStorage;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;

namespace SFA.DAS.AppStoreInsights.Shared.Extensions;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IHostBuilder AddAppStoreInsightsHost(this IHostBuilder builder, Action<HostBuilderContext, IServiceCollection>? additionalServices = null)
    {
        return builder
            .ConfigureFunctionsWorkerDefaults()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("local.settings.json", optional: true);

                var builtConfig = config.Build();
                config.AddAzureTableStorage(options =>
                {
                    options.ConfigurationKeys = builtConfig["ConfigNames"]?.Split(",") ?? Array.Empty<string>();
                    options.StorageConnectionString = builtConfig["ConfigurationStorageConnectionString"];
                    options.EnvironmentName = builtConfig["EnvironmentName"];
                    options.PreFixConfigurationKeys = false;
                });
            })
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton<IManagedIdentityTokenProvider, ManagedIdentityTokenProvider>();
                services.AddSingleton<IConnectionFactory, SqlServerConnectionFactory>();
                services.AddSingleton<IAppStoreRepository>(sp =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var connectionString = config["SqlConnectionString"]
                        ?? throw new InvalidOperationException("SqlConnectionString not found");
                    var connectionFactory = sp.GetRequiredService<IConnectionFactory>();
                    return new SqlAppStoreRepository(connectionFactory, connectionString);
                });
                services.AddHttpClient<IAppleStoreClient, AppleStoreClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        SslProtocols = SslProtocols.Tls12
                    });
                services.AddSingleton<IGooglePlayClient, GooglePlayClient>();

                // Additional services per function app
                additionalServices?.Invoke(context, services);
            });
    }
}