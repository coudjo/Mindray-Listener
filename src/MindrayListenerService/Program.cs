using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MindrayMiddleware;

var builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService() // no-op as a console app; registers as a service once installed
    .ConfigureServices((hostContext, services) =>
    {
        services.AddOptions<SerialSettings>()
            .Bind(hostContext.Configuration.GetSection("SerialSettings"))
            .ValidateOnStart();

        services.AddSingleton<IResultProcessor, LoggingResultProcessor>();
        services.AddHostedService<MindrayListenerWorker>();
    });

await builder.Build().RunAsync();