using Microsoft.EntityFrameworkCore;
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

        var connectionString = hostContext.Configuration.GetConnectionString("IziLabs");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<ListenerDbContext>(options => options.UseSqlServer(connectionString));
            services.AddSingleton<IResultProcessor, DatabaseResultProcessor>();
        }
        else
        {
            services.AddSingleton<IResultProcessor, LoggingResultProcessor>();
        }

        services.AddHostedService<MindrayListenerWorker>();
    });

await builder.Build().RunAsync();