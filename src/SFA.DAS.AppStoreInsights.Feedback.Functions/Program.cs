using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Configuration;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Extensions;
using SFA.DAS.AppStoreInsights.Shared.Clients;
using SFA.DAS.AppStoreInsights.Shared.Repositories;

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

        s.AddSingleton<IAppStoreRepository, SqlAppStoreRepository>();
        s.AddHttpClient<IAppleStoreClient, AppleStoreClient>();
        s.AddSingleton<IGooglePlayClient, GooglePlayClient>();
    })
    .Build();

await host.RunAsync();