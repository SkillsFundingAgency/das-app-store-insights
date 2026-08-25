using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Infrastructure;
using SFA.DAS.AppStoreInsights.Shared.Repositories;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Security.Authentication;

namespace SFA.DAS.AppStoreInsights.Shared.Extensions;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppStoreInsightsServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Add Managed Identity support
        services.AddSingleton<IManagedIdentityTokenProvider, ManagedIdentityTokenProvider>();
        services.AddSingleton<IConnectionFactory, SqlServerConnectionFactory>();

        // Register the repository with the connection string from configuration
        services.AddSingleton<IAppStoreRepository>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var connectionString = config["SqlConnectionString"]
                ?? throw new InvalidOperationException("SqlConnectionString not found");
            var connectionFactory = sp.GetRequiredService<IConnectionFactory>();
            return new SqlAppStoreRepository(connectionFactory, connectionString);
        });

        // Configure Apple client with TLS 1.2 support
        services.AddHttpClient<IAppleStoreClient, AppleStoreClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12
            });

        services.AddSingleton<IGooglePlayClient, GooglePlayClient>();

        return services;
    }
}