using FluentAssertions;

using NLog.Layouts;

namespace Nlog.RabbitMQ.Target.Tests;

public class NlogTargetConfigExtensionsTests
{
    private Func<Func<RabbitMqTarget, Layout>, string> LayoutRenderer(RabbitMqTarget target)
    {
        Func<Func<RabbitMqTarget, Layout>, string> funca = f =>
                                                              {
                                                                  Layout layout = f(target);
                                                                  return layout?.ToString() ?? string.Empty;
                                                              };

        return funca;
    }

    [Fact]
    public void GivenUriInConfig_WhenTargetInitializedFromConfig_ThenReturnValidRmqFactory()
    {
        // Arrange
        RabbitMqTarget target = new()
                                   {
                                       Uri = "amqp://guest:guest@localhost:5672"
                                   };

        // Act
        RabbitMqFactory factory = target.GetRabbitMqFactoryFromConfig(LayoutRenderer(target));

        // Assert
        factory.Should().NotBeNull();
    }

    [Fact]
    public void GivenHostnameInConfig_WhenTargetInitializedFromConfig_ThenReturnValidRmqFactory()
    {
        // Arrange
        RabbitMqTarget target = new()
                                   {
                                       HostName = "localhost",
                                       Port = "5672",
                                       UserName = "guest",
                                       Password = "guest",
                                       VHost = "/"
                                   };

        // Act
        RabbitMqFactory factory = target.GetRabbitMqFactoryFromConfig(LayoutRenderer(target));

        // Assert
        factory.Should().NotBeNull();
    }

    [Fact]
    public void GivenHostnameSInConfig_WhenTargetInitializedFromConfig_ThenReturnValidRmqFactory()
    {
        // Arrange
        RabbitMqTarget target = new()
                                   {
                                       HostName = "localhost, remotehost",
                                       Port = "5672",
                                       UserName = "guest",
                                       Password = "guest",
                                       VHost = "/",
                                       UseSsl = true,
                                       SslCertPath = "path/to/cert",
                                       SslCertPassphrase = "passphrase"
                                   };

        // Act
        RabbitMqFactory factory = target.GetRabbitMqFactoryFromConfig(LayoutRenderer(target));

        // Assert
        factory.Should().NotBeNull();
    }

    [Fact]
    public void GivenNoHostSpecifiedConfig_WhenTargetInitializedFromConfig_ThenThrowError()
    {
        // Arrange
        RabbitMqTarget target = new()
                                   {
                                       HostName = "localhost, remotehost",
                                       Port = "5672",
                                       UserName = "guest",
                                       Password = "guest",
                                       VHost = "/"
                                   };

        // Act
        RabbitMqFactory factory = target.GetRabbitMqFactoryFromConfig(LayoutRenderer(target));

        // Assert
        factory.Should().NotBeNull();
    }
}