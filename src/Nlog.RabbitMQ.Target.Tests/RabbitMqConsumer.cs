using System.Text;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NLog.Targets.RabbitMQ.Tests;

public class RabbitMqConsumer
{
    public string ExchangeName { get; }
    private readonly ConnectionFactory _factory;
    public string HostName => _factory.HostName;
    public int Port => _factory.Port;
    public string UserName => _factory.UserName;
    public string Password => _factory.Password;
    public string VirtualHost => _factory.VirtualHost;
    
    public RabbitMqConsumer(string connectionString, string exchangeName)
    {
        ExchangeName = exchangeName;
        _factory = new ConnectionFactory
                   {
                       Uri = new Uri(connectionString)
                   };
    }
    
    public async Task BindQueue(string queueName)
    {
        IConnection conn = await _factory.CreateConnectionAsync();
        IChannel channel = await conn.CreateChannelAsync();
        await channel.QueueDeclareAsync(queueName, false, false, false, null);
        await channel.QueueBindAsync(queueName, ExchangeName, string.Empty, null);
    }
    
    public async Task<bool> TryReceiveMessageAsync(string queueName)
    {
        var messageReceived = new TaskCompletionSource<bool>();
        IConnection conn = await _factory.CreateConnectionAsync();
        IChannel channel = await conn.CreateChannelAsync();
        AsyncEventingBasicConsumer consumer = new(channel);
        consumer.ReceivedAsync += async (model, ea) =>
                                  {
                                      messageReceived.SetResult(true);
                                      byte[] body = ea.Body.ToArray();
                                      string message = Encoding.UTF8.GetString(body);
                                      Console.WriteLine($"{message}");
                                      await channel.BasicAckAsync(ea.DeliveryTag, false);
                                  };
        await channel.BasicConsumeAsync(queueName, true, consumer);

        var taskCompeted = await Task.WhenAny(messageReceived.Task, Task.Delay(1000));
        return taskCompeted == messageReceived.Task;
    }
}
