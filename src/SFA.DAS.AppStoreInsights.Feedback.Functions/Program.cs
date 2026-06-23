using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SFA.DAS.AppStoreInsights.Feedback.Functions.Extensions;

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
            .AddApplicationRegistrations();
    })
    .Build();

await host.RunAsync();