using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Hosting;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
                               .ConfigureLogging((hostContext, logging) => logging.ClearProviders())
                               .UseNLog()
                               .ConfigureAppConfiguration(builder => builder.SetBasePath(Directory.GetCurrentDirectory())
                                                                            .AddJsonFile("appsettings.json")
                                                                            .AddEnvironmentVariables());
IHost host = hostBuilder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.Log(LogLevel.Information, "Starting application");

// Start a background task to produce random logs every second
var random = new Random();
var logLevels = new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical };
var cts = new CancellationTokenSource();

var backgroundTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        var level = logLevels[random.Next(logLevels.Length)];
        var message = $"Random log at {DateTime.Now:O} with level {level}";
        logger.Log(level, message);
        await Task.Delay(1, cts.Token);
    }
}, cts.Token);

await host.RunAsync();

cts.Cancel();
await backgroundTask;