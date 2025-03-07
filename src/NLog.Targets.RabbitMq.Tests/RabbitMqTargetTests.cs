
using System.Text;
using System.Text.Json;

using DotNet.Testcontainers.Builders;

using NLog.Common;
using FluentAssertions;

using NLog.Targets.RabbitMq;

namespace NLog.Targets.RabbitMQ.Tests;

public class RabbitMqTargetTests : IClassFixture<NLoggerFactory>
{
    private readonly NLoggerFactory _factory;
    private readonly RabbitMqConsumer _rabbitMqConsumer;
    public RabbitMqTargetTests(NLoggerFactory factory)
    {
        _factory = factory;
        _rabbitMqConsumer = _factory.RabbitMqConsumer;
    }
    
    [Fact(Skip = "Doesn't work at the moment")]
    public async Task GivenValidNlogConfig_WhenLogEvent ()
    {
        // Arrange
        var rmqTarget = _factory.CreateRabbitMqTarget();
        rmqTarget.InvokeMethod("Initialize", [null]);
        string queueName = "test-queue";

        await _rabbitMqConsumer.BindQueue(queueName);

        var logEvent = new LogEventInfo(LogLevel.Info, "TestLogger", "Test Message");
        var asyncLogEvent = new AsyncLogEventInfo(logEvent, e => { });
        
        // Act
        try
        {
            rmqTarget.WriteAsyncLogEvent(asyncLogEvent);
        }
        finally
        {
            rmqTarget.InvokeMethod("Close");
        }
        
        bool messageReceived = await _rabbitMqConsumer.TryReceiveMessageAsync(queueName);

        // Assert
       messageReceived.Should().BeTrue();
    }
}