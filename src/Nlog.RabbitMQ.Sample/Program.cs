using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Microsoft.Extensions.Hosting;

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

await host.RunAsync();

