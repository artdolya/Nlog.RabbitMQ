using System.Text.Json;

using FluentAssertions;

using NLog;

namespace Nlog.RabbitMQ.Target.Tests;

public class MessageFormatterTests
{
    [Fact]
    public void GivenLogEvent_WhenFormatMessage_ThenReturnFormattedMessage()
    {
        // Arrange
        string logMessage = "Test Message";
        LogLevel logLevel = LogLevel.Warn;
        string messageSource = "nlog://localhost/TestLogger";
        var logEvent = new LogEventInfo(logLevel, "TestLogger", logMessage);
        var messageFormatter = new MessageFormatter();
       
        // Act
        var formattedMessage = messageFormatter.GetMessage(logMessage, messageSource, logEvent, null, null);
        var deserializedMessage = JsonSerializer.Deserialize<LogLine>(formattedMessage);
        
        // Assert
        deserializedMessage.Should().NotBeNull();
        deserializedMessage.Message.Should().Be(logMessage);
        deserializedMessage.Level.Should().Be(logLevel.Name);
        deserializedMessage.Source.Should().Be("nlog://localhost/TestLogger");
        deserializedMessage.Type.Should().Be("amqp");
    }
}