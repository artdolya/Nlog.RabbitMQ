using DotNet.Testcontainers.Builders;

using NLog.Targets.RabbitMq;

using Testcontainers.RabbitMq;

namespace NLog.Targets.RabbitMQ.Tests;

public class NLoggerFactory : IAsyncLifetime
{
    private readonly RabbitMqContainer _container;
    private RabbitMqConsumer? _rabbitMqConsumer;
    public RabbitMqConsumer RabbitMqConsumer
    {
        get
        {
            if (_rabbitMqConsumer == null)
            {
                throw new InvalidOperationException("RabbitMqConsumer is not initialized");
            }
            
            return _rabbitMqConsumer;
        }
    }

    public NLoggerFactory()
    {
        RabbitMqBuilder? rmqBuilder = new RabbitMqBuilder()
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672));
        _container = rmqBuilder.Build();
    }

    private string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _rabbitMqConsumer =
            new RabbitMqConsumer(ConnectionString, "test-exchange");
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
    }

    public RabbitMqTarget CreateRabbitMqTarget()
    {
        
        return new RabbitMqTarget
               {
                   VHost = RabbitMqConsumer.VirtualHost,
                   UserName = RabbitMqConsumer.UserName,
                   Password = RabbitMqConsumer.Password,
                   Port = RabbitMqConsumer.Port.ToString(),
                   HostName = RabbitMqConsumer.HostName,
                   UseJSON = true,
                   Exchange = RabbitMqConsumer.ExchangeName,
                   IncludeGdc = true,
                   IncludeScopeNested = true,
                   IncludeScopeProperties = true
               };
    }
}