using DotNet.Testcontainers.Builders;

using NLog.Targets.RabbitMq;

using Testcontainers.RabbitMq;

namespace NLog.Targets.RabbitMQ.Tests;

public class NLoggerFactory : IAsyncLifetime
{
    private readonly RabbitMqContainer _container;
    public RabbitMqConsumer RabbitMqConsumer;
    private readonly bool useRealRabbitMq = true;

    public NLoggerFactory()
    {
        if (!useRealRabbitMq)
        {
            RabbitMqBuilder? rmqBuilder = new RabbitMqBuilder()
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672));
            _container = rmqBuilder.Build();
        }
    }

    private string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (!useRealRabbitMq) 
            await _container.StartAsync();
        
        RabbitMqConsumer =
            new RabbitMqConsumer(useRealRabbitMq ? "amqp://docker:docker@localhost:5672/" : ConnectionString,
                                 "test-exchange");
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
    }

    public RabbitMqTarget CreateRabbitMqTarget()
    {
        return useRealRabbitMq
                   ? new RabbitMqTarget
                     {
                         Uri = "amqp://docker:docker@127.0.0.1:5672/",
                         Exchange = RabbitMqConsumer.ExchangeName,
                         VHost = "/",
                         UseJSON =true,
                         IncludeGdc=true,
                         IncludeScopeNested= true,
                         IncludeScopeProperties= true
                     }
                   : new RabbitMqTarget
                     {
                         VHost = RabbitMqConsumer.VirtualHost,
                         UserName = RabbitMqConsumer.UserName,
                         Password = RabbitMqConsumer.Password,
                         Port = RabbitMqConsumer.Port.ToString(),
                         HostName = RabbitMqConsumer.HostName,
                         UseJSON = true,
                         Exchange = RabbitMqConsumer.ExchangeName
                     };
    }
}