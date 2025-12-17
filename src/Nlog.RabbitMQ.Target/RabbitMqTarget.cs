using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nlog.RabbitMQ.Target
{
    /// <summary>
    ///     NLog target for writing to RabbitMQ Topic
    /// </summary>
    [Target("RabbitMq")]
    public class RabbitMqTarget : AsyncTaskTarget
    {
        public enum CompressionTypes
        {
            None,
            GZip
        }

        private readonly Encoding _encoding = Encoding.UTF8;

        private readonly DateTime _epoch = new(1970, 1, 1, 0, 0, 0, 0);
        private IChannel _channel;
        private IConnection _connection;
        private string _exchange;

        private RabbitMqFactory _factory;
        private MessageFormatter _messageFormatter;

        public RabbitMqTarget()
        {
            Layout = "${message}";
            Compression = CompressionTypes.None;
            Fields = new List<Field>();
        }

        protected override void InitializeTarget()
        {
            // Validate Fields configuration (RequiredParameterAttribute is obsolete in NLog v6+)
            if (Fields != null)
            {
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in Fields)
                {
                    if (field == null)
                        throw new NLogConfigurationException("Field configuration contains a null entry.");

                    if (string.IsNullOrEmpty(field.Key))
                        throw new NLogConfigurationException("Each <field> must specify a non-empty 'key'.");

                    if (field.Layout == null)
                        throw new NLogConfigurationException($"Field '{field.Key}' must specify a Layout.");

                    if (!seenKeys.Add(field.Key))
                        throw new NLogConfigurationException($"Duplicate field key '{field.Key}' in configuration.");
                }
            }

            LogEventInfo nullLogEvent = LogEventInfo.CreateNullEvent();
            _factory = this.GetRabbitMqFactoryFromConfig(f => RenderLogEvent(f(this), nullLogEvent));
            _connection = _factory.CreateConnectionAsync().GetAwaiter().GetResult();
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

            _messageFormatter = new MessageFormatter();
            _exchange = RenderLogEvent(Exchange, nullLogEvent);
            string exchangeType = RenderLogEvent(ExchangeType, nullLogEvent);

            try
            {
                _channel.ExchangeDeclarePassiveAsync(_exchange).GetAwaiter().GetResult();
            }
            catch (OperationInterruptedException ex)
                when (ex.ShutdownReason?.ReplyCode == 404) // NOT-FOUND (channel is now closed)
            {
                try { _channel?.DisposeAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
                _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

                _channel.ExchangeDeclareAsync(
                        _exchange,
                        exchangeType,
                        durable: Durable,
                        autoDelete: false,
                        arguments: null)
                    .GetAwaiter().GetResult();
            }
            catch (OperationInterruptedException ex)
                when (ex.ShutdownReason?.ReplyCode == 406) // PRECONDITION_FAILED (mismatch)
            {
                InternalLogger.Warn(ex, $"Exchange '{_exchange}' exists with different properties; using existing.");
            }

            base.InitializeTarget();
        }

        protected override async Task WriteAsyncTask(LogEventInfo logEvent, CancellationToken token)
        {
            string renderedMessage = RenderLogEvent(Layout, logEvent);
            string message = UseJSON ? GetSerializedString(renderedMessage, logEvent) : renderedMessage;

            string routingKey = RenderLogEvent(Topic, logEvent);
            if (routingKey.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                routingKey = routingKey.Replace("{0}", logEvent.Level.Name);

            var channel = await CreateChannelAsync().ConfigureAwait(false);
            var basicProperties = GetBasicProperties(logEvent);
            var bytes = _encoding.GetBytes(message);

            await channel.BasicPublishAsync(_exchange,
                                            routingKey,
                                            mandatory: false,   // avoid Basic.Return buffering unless you handle it
                                            basicProperties,
                                            Compression == CompressionTypes.GZip ? CompressMessage(bytes) : bytes,
                                            token).ConfigureAwait(false);
        }

        private readonly SemaphoreSlim _channelLock = new(1, 1);

        private async Task<IChannel> CreateChannelAsync()
        {
            await _channelLock.WaitAsync();
            try
            {
                if (_channel == null || !_channel.IsOpen)
                {
                    if (_channel != null)
                    {
                        try
                        {
                            await _channel.DisposeAsync();
                        }
                        catch (Exception e)
                        {
                            InternalLogger.Error("Error when disposing old channel", e);
                        }
                    }
                    _channel = await _connection.CreateChannelAsync();
                }
                return _channel;
            }
            finally
            {
                _channelLock.Release();
            }
        }

        private BasicProperties GetBasicProperties(LogEventInfo logEvent)
        {
            BasicProperties basicProperties = new();
            basicProperties.ContentEncoding = _encoding.EncodingName;
            string contentType = RenderLogEvent(ContentType, logEvent);
            if (!string.IsNullOrEmpty(contentType))
                basicProperties.ContentType = contentType;
            basicProperties.AppId = RenderLogEvent(AppId, logEvent);
            if (string.IsNullOrEmpty(basicProperties.AppId))
                basicProperties.AppId = logEvent.LoggerName;
            basicProperties.Timestamp = new AmqpTimestamp(Convert.ToInt64((logEvent.TimeStamp - _epoch).TotalSeconds));
            basicProperties.UserId = RenderLogEvent(UserName, logEvent);

            basicProperties.DeliveryMode = DeliveryMode == DeliveryMode.Persistent
                                               ? DeliveryModes.Persistent
                                               : DeliveryModes.Transient;
            string appCorrelationId = RenderLogEvent(CorrelationId, logEvent);
            if (!string.IsNullOrEmpty(appCorrelationId))
                basicProperties.CorrelationId = appCorrelationId;
            string appMessageType = RenderLogEvent(MessageType, logEvent);
            if (!string.IsNullOrEmpty(appMessageType))
                basicProperties.Type = appMessageType;
            return basicProperties;
        }

        private string GetSerializedString(string renderedMessage, LogEventInfo logEvent)
        {
            var contextProperties = GetFullContextProperties(logEvent);
            string messageSource = RenderLogEvent(MessageSource, logEvent);
            if (string.IsNullOrEmpty(messageSource))
                // initialize messageSource with default value
                messageSource = $"nlog://{Dns.GetHostName()}/{logEvent.LoggerName}";

            return _messageFormatter.GetMessage(renderedMessage, messageSource, logEvent, Fields, contextProperties);
        }

        private IDictionary<string, object> GetFullContextProperties(LogEventInfo logEvent)
        {
            var allProperties = GetContextProperties(logEvent);
            if (IncludeScopeNested)
            {
                var ndlcProperties = GetScopeContextNested(logEvent);
                if (ndlcProperties.Count > 0)
                {
                    allProperties = allProperties ?? new Dictionary<string, object>();
                    allProperties.Add("ndlc", ndlcProperties);
                }
            }

            return allProperties;
        }

        private byte[] CompressMessage(byte[] messageBytes)
        {
            switch (Compression)
            {
                case CompressionTypes.None:
                    return messageBytes;
                case CompressionTypes.GZip:
                    return CompressMessageGZip(messageBytes);
                default:
                    throw new NLogConfigurationException($"Compression type '{Compression}' not supported.");
            }
        }

        /// <summary>
        ///     Compresses bytes using GZip data format
        /// </summary>
        /// <param name="messageBytes"></param>
        /// <returns></returns>
        private byte[] CompressMessageGZip(byte[] messageBytes)
        {
            using var ms = new MemoryStream();
            using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            {
                gzip.Write(messageBytes, 0, messageBytes.Length);
            }
            return ms.ToArray();
        }

        protected override void CloseTarget()
        {
            // Ensure all resources are disposed synchronously
            try
            {
                _channelLock.Wait();
                try
                {
                    if (_channel != null)
                    {
                        if (_channel.IsOpen)
                        {
                            _channel.CloseAsync().GetAwaiter().GetResult();
                        }
                        _channel.DisposeAsync().GetAwaiter().GetResult();
                        _channel = null;
                    }
                }
                finally { _channelLock.Release(); }
            }
            catch (Exception e)
            {
                InternalLogger.Error("Error when closing/disposing channel", e);
            }

            try
            {
                if (_connection != null)
                {
                    if (_connection.IsOpen)
                    {
                        _connection.CloseAsync().GetAwaiter().GetResult();
                    }
                    _connection.DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    _connection = null;
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error("Error when closing/disposing connection", e);
            }
            base.CloseTarget();
        }

        #region Layout Properties

        /// <summary>
        ///     Gets or sets the uri to use for the connection (Alternative to the individual connection-properties)
        /// </summary>
        /// <remarks>
        ///     amqp://myuser:mypass@myrabbitserver:5672/filestream
        /// </remarks>
        public Layout Uri { get; set; }

        /// <summary>
        ///     Gets or sets the virtual host to publish to.
        /// </summary>
        public Layout VHost { get; set; } = "/";

        /// <summary>
        ///     Gets or sets the username to use for
        ///     authentication with the message broker. The default
        ///     is 'guest'
        /// </summary>
        public Layout UserName { get; set; } = "guest";

        /// <summary>
        ///     Gets or sets the password to use for
        ///     authentication with the message broker.
        ///     The default is 'guest'
        /// </summary>
        public Layout Password { get; set; } = "guest";

        /// <summary>
        ///     Gets or sets the port to use
        ///     for connections to the message broker (this is the broker's
        ///     listening port).
        ///     The default is '-1' (Resolves to 5672, or 5671 when UseSsl = true)
        /// </summary>
        public Layout Port { get; set; } = AmqpTcpEndpoint.UseDefaultPort.ToString();

        /// <summary>
        ///     Gets or sets the routing key (aka. topic) with which
        ///     to send messages. Defaults to {0}, which in the end is 'error' for log.Error("..."), and
        ///     so on. An example could be setting this property to 'ApplicationType.MyApp.Web.{0}'.
        ///     The default is '{0}'.
        /// </summary>
        public Layout Topic { get; set; } = "${level}";

        /// <summary>
        ///     Gets or sets the AMQP protocol (version) to use
        ///     for communications with the RabbitMQ broker. The default
        ///     is the RabbitMQ.Client-library's default protocol.
        /// </summary>
        public IProtocol Protocol { get; set; } = Protocols.DefaultProtocol;

        /// <summary>
        ///     Gets or sets the host name of the broker to log to.
        /// </summary>
        /// <remarks>
        ///     Default is 'localhost'
        /// </remarks>
        public Layout HostName { get; set; } = "localhost";

        /// <summary>
        ///     Gets or sets the exchange to bind the logger output to.
        /// </summary>
        /// <remarks>
        ///     Default is 'app-logging'
        /// </remarks>
        public Layout Exchange { get; set; } = "app-logging";

        /// <summary>
        ///     Gets or sets the exchange type to bind the logger output to.
        /// </summary>
        /// <remarks>
        ///     Default is 'topic'
        /// </remarks>
        public Layout ExchangeType { get; set; } = "topic";

        /// <summary>
        ///     Gets or sets the Application-specific connection name, will be displayed in the management UI
        ///     if RabbitMQ server supports it. This value doesn't have to be unique and cannot
        ///     be used as a connection identifier, e.g. in HTTP API requests. This value is
        ///     supposed to be human-readable.
        /// </summary>
        public Layout ClientProvidedName { get; set; }

        /// <summary>
        ///     Gets or sets the setting specifying whether the exchange
        ///     is durable (persisted across restarts)
        /// </summary>
        /// <remarks>
        ///     Default is true
        /// </remarks>
        public bool Durable { get; set; } = true;

        /// <summary>
        ///     Gets or sets the setting specifying whether the exchange
        ///     should be declared or used passively.
        /// </summary>
        /// <remarks>
        ///     Default is false
        /// </remarks>
        public bool Passive { get; set; }

        /// <summary>
        ///     Gets or sets the application id to specify when sending. <see cref="IBasicProperties.AppId" />. Defaults to null,
        ///     and then IBasicProperties.AppId will be the name of the logger instead.
        /// </summary>
        public Layout AppId { get; set; }

        /// <summary>
        ///     Gets or sets the Application correlation identifier <see cref="IBasicProperties.CorrelationId" />. Defaults to
        ///     null,
        /// </summary>
        public Layout CorrelationId { get; set; }

        /// <summary>
        ///     Gets or sets the Application message type name <see cref="IBasicProperties.Type" />. Defaults to null,
        /// </summary>
        public Layout MessageType { get; set; }

        /// <summary>
        ///     Gets or sets the Layout for rendering <see cref="LogLine.Source" /> when enabling <see cref="UseJSON" />
        /// </summary>
        public Layout MessageSource { get; set; } = "nlog://${hostname}/${logger}";

        /// <summary>
        ///     Gets or sets the ContentType to specify when sending. <see cref="IBasicProperties.ContentType" /> - MIME
        ///     ContentType. Defaults to text/plain
        /// </summary>
        public Layout ContentType { get; set; } = "text/plain";

        /// <summary>
        ///     Gets or sets the maximum number of messages to save in the case
        ///     that the RabbitMQ instance goes down. Must be >= 1. Defaults to 10240.
        /// </summary>
        public int MaxBuffer { get; set; } = 10240;

        /// <summary>
        ///     Gets or sets the number of heartbeat seconds to have for the RabbitMQ connection.
        ///     If the heartbeat times out, then the connection is closed (logically) and then
        ///     re-opened the next time a log message comes along.
        /// </summary>
        public ushort HeartBeatSeconds { get; set; } = 3;

        /// <summary>
        ///     Gets or sets whether to format the data in the body as a JSON structure.
        ///     Having it as a JSON structure means that you can more easily interpret the data
        ///     at its final resting place, than if it were a simple string - i.e. you don't
        ///     have to mess with advanced parsers if you have this options for all of your
        ///     applications. A product that you can use for viewing logs
        ///     generated is logstash (https://logstash.net), elasticsearch (https://github.com/elasticsearch/elasticsearch)
        ///     and kibana (https://rashidkpc.github.com/Kibana/)
        /// </summary>
        public bool UseJSON
        {
            get => ContentType?.ToString()?.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;
            set => ContentType = value ? "application/json" : "text/plain";
        }

        /// <summary>
        ///     Enables TLS support to connect to the Message Queue (AMQP over TLS).
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>
        ///     Location of client SSL certificate for mutual TLS.
        ///     See https://www.rabbitmq.com/docs/ssl#dotnet-client
        /// </summary>
        public Layout SslCertPath { get; set; }

        /// <summary>
        ///     Passphrase for generated SSL certificate defined in SslCertPath for mutual TLS.
        ///     See https://www.rabbitmq.com/docs/ssl#dotnet-client
        /// </summary>
        public Layout SslCertPassphrase { get; set; }

        /// <summary>
        ///     The delivery more, 1 for non-persistent, 2 for persistent
        /// </summary>
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.NonPersistent;

        /// <summary>
        ///     The amount of milliseconds to wait when starting a connection
        ///     before moving on to next task
        /// </summary>
        public int Timeout { get; set; } = 3000;

        /// <summary>
        ///     Gets or sets compression type.
        ///     Available compression methods: None, GZip
        /// </summary>
        public CompressionTypes Compression { get; set; }

        [ArrayParameter(typeof(Field), "field")]
        public IList<Field> Fields { get; private set; }

        #endregion
    }
}