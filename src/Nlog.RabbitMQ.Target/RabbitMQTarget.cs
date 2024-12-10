using Newtonsoft.Json;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nlog.RabbitMQ.Target
{
    /// <summary>
    /// NLog target for writing to RabbitMQ Topic
    /// </summary>
    [Target("RabbitMQ")]
    public class RabbitMQTarget : TargetWithContext
    {
        public enum CompressionTypes
        {
            None,
            GZip
        };

        private IConnection _Connection;
        private IChannel _Channel;
        private string _ModelExchange;
        private readonly Encoding _Encoding = Encoding.UTF8;

        private readonly Queue<Tuple<byte[], BasicProperties, Func<BasicProperties, BasicProperties>, string>> _UnsentMessages
            = new Queue<Tuple<byte[], BasicProperties, Func<BasicProperties, BasicProperties>, string>>(512);

        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);

        private JsonSerializer JsonSerializer => _jsonSerializer ?? (_jsonSerializer = JsonSerializer.Create(MessageFormatter.CreateJsonSerializerSettings()));
        private JsonSerializer _jsonSerializer;

        private readonly char[] _reusableEncodingBuffer = new char[32 * 1024];  // Avoid large-object-heap

        public RabbitMQTarget()
        {
            Layout = "${message}";
            Compression = CompressionTypes.None;
            Fields = new List<Field>();
        }

        #region Properties

        /// <summary>
        /// 	Gets or sets the uri to use for the connection (Alternative to the individual connection-properties)
        /// </summary>
        /// <remarks>
        ///		amqp://myuser:mypass@myrabbitserver:5672/filestream
        /// </remarks>
        public Layout Uri { get; set; }

        /// <summary>
        /// 	Gets or sets the virtual host to publish to.
        /// </summary>
        public Layout VHost { get; set; } = "/";

        /// <summary>
        /// 	Gets or sets the username to use for
        /// 	authentication with the message broker. The default
        /// 	is 'guest'
        /// </summary>
        public Layout UserName { get; set; } = "guest";

        /// <summary>
        /// 	Gets or sets the password to use for
        /// 	authentication with the message broker.
        /// 	The default is 'guest'
        /// </summary>
        public Layout Password { get; set; } = "guest";

        /// <summary>
        /// 	Gets or sets the port to use
        /// 	for connections to the message broker (this is the broker's
        /// 	listening port).
        /// 	The default is '-1' (Resolves to 5672, or 5671 when UseSsl = true)
        /// </summary>
        public Layout Port { get; set; } = AmqpTcpEndpoint.UseDefaultPort.ToString();

        ///<summary>
        ///	Gets or sets the routing key (aka. topic) with which
        ///	to send messages. Defaults to {0}, which in the end is 'error' for log.Error("..."), and
        ///	so on. An example could be setting this property to 'ApplicationType.MyApp.Web.{0}'.
        ///	The default is '{0}'.
        ///</summary>
        public Layout Topic { get; set; } = "${level}";

        /// <summary>
        /// 	Gets or sets the AMQP protocol (version) to use
        /// 	for communications with the RabbitMQ broker. The default 
        /// 	is the RabbitMQ.Client-library's default protocol.
        /// </summary>
        public IProtocol Protocol { get; set; } = Protocols.DefaultProtocol;

        /// <summary>
        /// 	Gets or sets the host name of the broker to log to.
        /// </summary>
        /// <remarks>
        /// 	Default is 'localhost'
        /// </remarks>
        public Layout HostName { get; set; } = "localhost";

        /// <summary>
        /// 	Gets or sets the exchange to bind the logger output to.
        /// </summary>
        /// <remarks>
        /// 	Default is 'app-logging'
        /// </remarks>
        public Layout Exchange { get; set; } = "app-logging";

        /// <summary>
        ///		Gets or sets the exchange type to bind the logger output to.
        /// </summary>
        /// <remarks>
        ///   Default is 'topic'
        /// </remarks>
        public Layout ExchangeType { get; set; } = "topic";

        /// <summary>
        ///		Gets or sets the Application-specific connection name, will be displayed in the management UI
        ///		if RabbitMQ server supports it. This value doesn't have to be unique and cannot
        ///		be used as a connection identifier, e.g. in HTTP API requests. This value is
        ///		supposed to be human-readable.
        /// </summary>
        public Layout ClientProvidedName { get; set; }

        /// <summary>
        /// 	Gets or sets the setting specifying whether the exchange 
        ///		is durable (persisted across restarts)
        /// </summary>
        /// <remarks>
        /// 	Default is true
        /// </remarks>
        public bool Durable { get; set; } = true;

        /// <summary>
        /// 	Gets or sets the setting specifying whether the exchange
        ///     should be declared or used passively.
        /// </summary>
        /// <remarks>
        /// 	Default is false
        /// </remarks>
        public bool Passive { get; set; }

        /// <summary>
        /// 	Gets or sets the application id to specify when sending. <see cref="IBasicProperties.AppId" />. Defaults to null,
        /// 	and then IBasicProperties.AppId will be the name of the logger instead.
        /// </summary>
        public Layout AppId { get; set; }

        /// <summary>
        ///		Gets or sets the Application correlation identifier <see cref="IBasicProperties.CorrelationId" />. Defaults to null,
        /// </summary>
        public Layout CorrelationId { get; set; }

        /// <summary>
        ///		Gets or sets the Application message type name <see cref="IBasicProperties.Type" />. Defaults to null,
        /// </summary>
        public Layout MessageType { get; set; }

        /// <summary>
        ///		Gets or sets the Layout for rendering <see cref="LogLine.Source"/> when enabling <see cref="UseJSON"/>
        /// </summary>
        public Layout MessageSource { get; set; } = "nlog://${hostname}/${logger}";

        /// <summary>
        /// Gets or sets the ContentType to specify when sending. <see cref="IBasicProperties.ContentType" /> - MIME ContentType. Defaults to text/plain
        /// </summary>
        public Layout ContentType { get; set; } = "text/plain";

        /// <summary>
        /// Gets or sets the maximum number of messages to save in the case
        /// that the RabbitMQ instance goes down. Must be >= 1. Defaults to 10240.
        /// </summary>
        public int MaxBuffer { get; set; } = 10240;

        /// <summary>
        /// Gets or sets the number of heartbeat seconds to have for the RabbitMQ connection.
        /// If the heartbeat times out, then the connection is closed (logically) and then
        /// re-opened the next time a log message comes along.
        /// </summary>
        public ushort HeartBeatSeconds { get; set; } = 3;

        /// <summary>
        /// Gets or sets whether to format the data in the body as a JSON structure.
        /// Having it as a JSON structure means that you can more easily interpret the data
        /// at its final resting place, than if it were a simple string - i.e. you don't
        /// have to mess with advanced parsers if you have this options for all of your
        /// applications. A product that you can use for viewing logs
        /// generated is logstash (http://logstash.net), elasticsearch (https://github.com/elasticsearch/elasticsearch)
        /// and kibana (http://rashidkpc.github.com/Kibana/)
        /// </summary>
        public bool UseJSON
        {
            get => ContentType?.ToString()?.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;
            set => ContentType = value ? "application/json" : "text/plain";
        }

        /// <summary>
        /// Enables SSL support to connect to the Message Queue. If this is enabled, 
        /// SslCertPath and SslCertPassphrase are required! For more information please
        /// visit http://www.rabbitmq.com/ssl.html
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>
        /// Location of client SSL certificate
        /// </summary>
        public Layout SslCertPath { get; set; }

        /// <summary>
        /// Passphrase for generated SSL certificate defined in SslCertPath
        /// </summary>
        public Layout SslCertPassphrase { get; set; }

        /// <summary>
        /// The delivery more, 1 for non-persistent, 2 for persistent
        /// </summary>
        public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.NonPersistent;

        /// <summary>
        /// The amount of milliseconds to wait when starting a connection
        /// before moving on to next task
        /// </summary>
        public int Timeout { get; set; } = 3000;

        /// <summary>
        /// Gets or sets compression type. 
        /// Available compression methods: None, GZip
        /// </summary>
        public CompressionTypes Compression { get; set; }

        [ArrayParameter(typeof(Field), "field")]
        public IList<Field> Fields { get; private set; }

        /// <summary>
        /// Using for JSON formating (when UseJSON set true). 
        /// If set as true - <see cref="Message"/> field rendered by Layout prorerty instead getting <see cref="LogEventInfo.FormattedMessage"/>
        /// </summary>
        [Obsolete("Message-field now always uses Layout, and default Layout is ${message}")]
        public bool UseLayoutAsMessage { get; set; } = true;

        #endregion

        protected override void Write(LogEventInfo logEvent)
        {
            var uncompressedMessage = GetMessage(logEvent);
            var message = CompressMessage(uncompressedMessage);

            var routingKey = RenderLogEvent(Topic, logEvent);
            if (routingKey.IndexOf("{0}") >= 0)
                routingKey = routingKey.Replace("{0}", logEvent.Level.Name);

            var model = _Channel;
            var modelExchange = _ModelExchange;
            if (model == null || !model.IsOpen)
            {
                StartConnection(_Connection, Timeout, true);
                model = _Channel;
                modelExchange = _ModelExchange;
            }

            var basicProperties = model != null ? GetBasicProperties(logEvent, model) : null;
            if (model == null || !model.IsOpen)
            {
                if (!AddUnsent(logEvent, routingKey, basicProperties, message))
                {
                    throw new InvalidOperationException("LogEvent discarded because RabbitMQ instance is offline and reached MaxBuffer");
                }
                return;
            }

            bool restartConnection = true;

            try
            {
                RunTaskSync(async () =>
                {
                    var token = CancellationToken.None;
                    await CheckUnsent(model, modelExchange, token);
                    await Publish(model, message, basicProperties, routingKey, modelExchange, token);
                });
                restartConnection = false;
            }
            catch (IOException e)
            {
                InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not send to RabbitMQ instance: {1}", Name, e.Message);
                if (!AddUnsent(logEvent, routingKey, basicProperties, message))
                    throw;
            }
            catch (ObjectDisposedException e)
            {
                InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not send to RabbitMQ instance: {1}", Name, e.Message);
                if (!AddUnsent(logEvent, routingKey, basicProperties, message))
                    throw;
            }
            catch (Exception e)
            {
                restartConnection = false;    // Skip connection reconnect, maybe the LogEvent is broken, or maybe halfway there
                InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not send to RabbitMQ instance: {1}", Name, e.Message);
                throw;
            }
            finally
            {
                if (restartConnection)
                {
                    StartConnection(_Connection, Math.Min(500, Timeout), true);
                }
            }
        }

        private bool AddUnsent(LogEventInfo logEvent, string routingKey, BasicProperties basicProperties, byte[] message)
        {
            if (_UnsentMessages.Count < MaxBuffer)
            {
                Func<BasicProperties, BasicProperties> propertyResolver = (props) => props;
                if (basicProperties == null)
                    propertyResolver = (props) => _Channel != null ? GetBasicProperties(logEvent, _Channel) : null;
                _UnsentMessages.Enqueue(Tuple.Create(message, basicProperties, propertyResolver, routingKey));
                return true;
            }
            else
            {
                InternalLogger.Warn("RabbitMQTarget(Name={0}): MaxBuffer {1} filled. Ignoring message.", Name, MaxBuffer);
                return false;
            }
        }

        private async Task CheckUnsent(IChannel model, string exchange, CancellationToken cancellationToken)
        {
            // using a queue so that removing and publishing is a single operation
            while (_UnsentMessages.Count > 0)
            {
                var tuple = _UnsentMessages.Dequeue();
                InternalLogger.Info("RabbitMQTarget(Name={0}): Publishing unsent message: {1}.", Name, tuple);
                var basicProperties = tuple.Item3.Invoke(tuple.Item2);
                await Publish(model, tuple.Item1, basicProperties, tuple.Item4, exchange, cancellationToken);
            }
        }

        private async Task Publish(IChannel model, byte[] bytes, BasicProperties basicProperties, string routingKey, string exchange, CancellationToken cancellationToken)
        {
            await model.BasicPublishAsync(exchange,
                routingKey,
                true, basicProperties,
                bytes,
                cancellationToken);
        }

        private byte[] GetMessage(LogEventInfo logEvent)
        {
            var msg = GetMessageString(logEvent);

            if (msg.Length < _reusableEncodingBuffer.Length)
            {
                lock (_reusableEncodingBuffer)
                {
                    msg.CopyTo(0, _reusableEncodingBuffer, 0, msg.Length);
                    return _Encoding.GetBytes(_reusableEncodingBuffer, 0, msg.Length);
                }
            }
            else
            {
                return _Encoding.GetBytes(msg);   // Calls string.ToCharArray()
            }
        }

        private string GetMessageString(LogEventInfo logEvent)
        {
            if (!UseJSON)
            {
                return RenderLogEvent(Layout, logEvent);
            }
            else
            {
                var message = RenderLogEvent(Layout, logEvent);
                var contextProperties = GetFullContextProperties(logEvent);
                var messageSource = RenderLogEvent(MessageSource, logEvent);
                if (string.IsNullOrEmpty(messageSource))
                    messageSource = string.Format("nlog://{0}/{1}", System.Net.Dns.GetHostName(), logEvent.LoggerName);

                try
                {
                    var jsonSerializer = JsonSerializer;
                    lock (jsonSerializer)
                    {
                        return MessageFormatter.GetMessageInner(jsonSerializer, message, messageSource, logEvent, Fields, contextProperties);
                    }
                }
                catch (Exception e)
                {
                    _jsonSerializer = null; // reset as it it might be broken
                    InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Failed to serialize LogEvent: {1}", Name, e.Message);
                    throw;
                }
            }
        }

        private IDictionary<string, object> GetFullContextProperties(LogEventInfo logEvent)
        {
            var allProperties = GetContextProperties(logEvent);
            if (IncludeNdlc)
            {
                var ndlcProperties = GetContextNdlc(logEvent);
                if (ndlcProperties.Count > 0)
                {
                    allProperties = allProperties ?? new Dictionary<string, object>();
                    allProperties.Add("ndlc", ndlcProperties);
                }
            }

            return allProperties;
        }

        private BasicProperties GetBasicProperties(LogEventInfo logEvent, IChannel channel)
        {
            var basicProperties = new BasicProperties();
            basicProperties.ContentEncoding = "utf8";
            var contentType = RenderLogEvent(ContentType, logEvent);
            if (!string.IsNullOrEmpty(contentType))
                basicProperties.ContentType = contentType;
            basicProperties.AppId = RenderLogEvent(AppId, logEvent);
            if (string.IsNullOrEmpty(basicProperties.AppId))
                basicProperties.AppId = logEvent.LoggerName;
            basicProperties.Timestamp = new AmqpTimestamp(MessageFormatter.GetEpochTimeStamp(logEvent));
            basicProperties.UserId = RenderLogEvent(UserName, logEvent);   // support Validated User-ID (see http://www.rabbitmq.com/extensions.html)
            basicProperties.DeliveryMode = DeliveryMode == DeliveryMode.Persistent ? DeliveryModes.Persistent : DeliveryModes.Transient;
            var appCorrelationId = RenderLogEvent(CorrelationId, logEvent);
            if (!string.IsNullOrEmpty(appCorrelationId))
                basicProperties.CorrelationId = appCorrelationId;
            var appMessageType = RenderLogEvent(MessageType, logEvent);
            if (!string.IsNullOrEmpty(appMessageType))
                basicProperties.Type = appMessageType;
            return basicProperties;
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();
            StartConnection(_Connection, Timeout, false);
        }

        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            if (_Channel?.IsOpen == true)
            {
                RunTaskSync(async () => await CheckUnsent(_Channel, _ModelExchange, CancellationToken.None));
            }

            base.FlushAsync(asyncContinuation);
        }

        /// <summary>
        /// Never throws
        /// </summary>
        private void StartConnection(IConnection oldConnection, int timeoutMilliseconds, bool checkInitialized)
        {
            if (!ReferenceEquals(oldConnection, _Connection) && (_Channel?.IsOpen ?? false))
                return;

            var t = Task.Run(async () =>
            {
                // Do not lock on SyncRoot, as we are waiting on this task while holding SyncRoot-lock
                _semaphoreSlim.Wait();
                try
                {
                    if (checkInitialized && !IsInitialized)
                        return;

                    if (!ReferenceEquals(oldConnection, _Connection) && (_Channel?.IsOpen ?? false))
                        return;

                    InternalLogger.Info("RabbitMQTarget(Name={0}): Connection attempt started...", Name);
                    oldConnection = _Connection ?? oldConnection;
                    if (oldConnection != null)
                    {
                        await ShutdownAmqp(oldConnection, 504 /*Constants.ChannelError*/, "Model not open to RabbitMQ instance");
                    }

                    IChannel channel = null;
                    IConnection connection = null;
                    string exchange = null;

                    try
                    {
                        var factory = GetConnectionFactory(out exchange, out var exchangeType, out var hostNames);
                        connection = hostNames?.Count > 0 ? await factory.CreateConnectionAsync(hostNames) : await factory.CreateConnectionAsync();
                        connection.ConnectionShutdownAsync += (s, e) => ShutdownAmqp(ReferenceEquals(_Connection, connection) ? connection : null, e.ReplyCode, e.ReplyText);

                        try
                        {
                            channel = await connection.CreateChannelAsync();
                        }
                        catch (Exception e)
                        {
                            InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not create model, {1}", Name, e.Message);
                            throw;
                        }

                        if (channel != null && !Passive)
                        {
                            try
                            {
                                await channel.ExchangeDeclareAsync(exchange, exchangeType, Durable);
                            }
                            catch (Exception e)
                            {
                                InternalLogger.Error(e, string.Format("RabbitMQTarget(Name={0}): Could not declare exchange={1}. Error={2}", Name, exchange, e.Message));
                                throw;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        var shutdownConnection = connection;
                        connection = null;

                        if (shutdownConnection == null)
                        {
                            InternalLogger.Error(e, string.Format("RabbitMQTarget(Name={0}): Could not connect to RabbitMQ instance: {1}", Name, e.Message));
                        }
                        else
                        {
                            channel?.Dispose();
                            channel = null;
                            await shutdownConnection.CloseAsync(TimeSpan.FromMilliseconds(1000));
                            await shutdownConnection.AbortAsync(TimeSpan.FromMilliseconds(1000));
                        }
                    }
                    finally
                    {
                        if (connection != null && channel != null)
                        {
                            if ((_Channel?.IsOpen ?? false))
                            {
                                InternalLogger.Info("RabbitMQTarget(Name={0}): Connection attempt completed succesfully, but not needed", Name);
                            }
                            else
                            {
                                _Connection = connection;
                                _Channel = channel;
                                _ModelExchange = exchange;
                                InternalLogger.Info("RabbitMQTarget(Name={0}): Connection attempt completed succesfully", Name);
                            }
                        }
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            });

            var completedTask = Task.WhenAny(t, Task.Delay(TimeSpan.FromMilliseconds(timeoutMilliseconds))).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!ReferenceEquals(completedTask, t))
            {
                InternalLogger.Warn("RabbitMQTarget(Name={0}): Connection timeout to RabbitMQ instance after {1}ms", Name, timeoutMilliseconds);
            }
            else if (completedTask.Exception != null)
            {
                InternalLogger.Error(completedTask.Exception, "RabbitMQTarget(Name={0}): Connection attempt to RabbitMQ instance failed: {1}", Name, completedTask.Exception.Message);
            }
        }

        private ConnectionFactory GetConnectionFactory(out string exchange, out string exchangeType, out IList<AmqpTcpEndpoint> hostNames)
        {
            var nullLogEvent = LogEventInfo.CreateNullEvent();

            exchange = RenderLogEvent(Exchange, nullLogEvent);
            exchangeType = RenderLogEvent(ExchangeType, nullLogEvent);
            var clientProvidedName = RenderLogEvent(ClientProvidedName, nullLogEvent);

            var uriString = RenderLogEvent(Uri, nullLogEvent);
            var hostName = RenderLogEvent(HostName, nullLogEvent) ?? string.Empty;
            var port = Convert.ToInt32(RenderLogEvent(Port, nullLogEvent));
            var vHost = RenderLogEvent(VHost, nullLogEvent);
            var userName = RenderLogEvent(UserName, nullLogEvent);
            var password = RenderLogEvent(Password, nullLogEvent);
            var sslCertPath = RenderLogEvent(SslCertPath, nullLogEvent);
            var sslCertPassphrase = RenderLogEvent(SslCertPassphrase, nullLogEvent);

            if (hostName.IndexOf(',') >= 0)
            {
                hostNames = new List<AmqpTcpEndpoint>();
                foreach (var host in hostName.Split(','))
                {
                    if (string.IsNullOrWhiteSpace(host))
                        continue;

                    var endPoint = new AmqpTcpEndpoint()
                    {
                        HostName = host.Trim(),
                        Port = port,
                    };

                    if (UseSsl)
                    {
                        endPoint.Ssl = ResolveSslOption(endPoint.HostName, sslCertPath, sslCertPassphrase);
                    }

                    hostNames.Add(endPoint);
                }
            }
            else
            {
                hostNames = Array.Empty<AmqpTcpEndpoint>();
            }

            var factory = new ConnectionFactory
            {
                VirtualHost = vHost,
                UserName = userName,
                Password = password,
                RequestedHeartbeat = TimeSpan.FromSeconds(HeartBeatSeconds),
                Port = port,
            };

            if (!string.IsNullOrEmpty(hostName) && hostNames.Count == 0)
            {
                factory.HostName = hostName.Trim();
                if (UseSsl)
                {
                    factory.Ssl = ResolveSslOption(factory.HostName, sslCertPath, sslCertPassphrase);
                }
            }

            if (!string.IsNullOrEmpty(clientProvidedName))
            {
                factory.ClientProvidedName = clientProvidedName;
            }

            if (!string.IsNullOrWhiteSpace(uriString))
            {
                factory.Uri = new System.Uri(uriString);    // Extracts connection properties from Uri
            }

            return factory;
        }

        private SslOption ResolveSslOption(string serverName, string sslCertPath, string sslCertPassphrase)
        {
            var sslOption = new SslOption()
            {
                Enabled = true,
                ServerName = serverName,
            };
            if (!string.IsNullOrEmpty(sslCertPath))
            {
                sslOption.CertPath = sslCertPath;
            }
            if (!string.IsNullOrEmpty(sslCertPassphrase))
            {
                sslOption.CertPassphrase = sslCertPassphrase;
            }
            return sslOption;
        }

        private async Task ShutdownAmqp(IConnection connection, ushort reasonReplyCode, string reasonReplyText)
        {
            if (reasonReplyCode != 200 /* Constants.ReplySuccess*/)
            {
                InternalLogger.Warn("RabbitMQTarget(Name={0}): Connection shutdown. ReplyCode={1}, ReplyText={2}", Name, reasonReplyCode, reasonReplyText);
            }
            else
            {
                InternalLogger.Info("RabbitMQTarget(Name={0}): Connection shutdown. ReplyCode={1}, ReplyText={2}", Name, reasonReplyCode, reasonReplyText);
            }

            _semaphoreSlim.Wait();
            try
            {
                if (connection != null)
                {
                    IChannel channel = null;

                    if (ReferenceEquals(connection, _Connection))
                    {
                        channel = _Channel;
                        _Connection = null;
                        _Channel = null;
                    }

                    try
                    {
                        if (reasonReplyCode == 200 /* Constants.ReplySuccess*/ && connection.IsOpen)
                            await channel?.CloseAsync();
                        else
                            await channel?.AbortAsync(); // Abort is close without throwing Exceptions
                    }
                    catch (Exception e)
                    {
                        InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not close model: {1}", Name, e.Message);
                    }

                    try
                    {
                        // you get 1.5 seconds to shut down!
                        if (reasonReplyCode == 200 /* Constants.ReplySuccess*/ && connection.IsOpen)
                            await connection.CloseAsync(reasonReplyCode, reasonReplyText, TimeSpan.FromMilliseconds(1500));
                        else
                            await connection.AbortAsync(reasonReplyCode, reasonReplyText, TimeSpan.FromMilliseconds(1500));
                    }
                    catch (Exception e)
                    {
                        InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not close connection: {1}", Name, e.Message);
                    }
                }
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        // Dispose calls CloseTarget!
        protected override void CloseTarget()
        {
            if (_UnsentMessages.Count > 0)
            {
                InternalLogger.Warn("RabbitMQTarget(Name={0}): Closing but still {1} unsent messages, because RabbitMQ instance is offline", Name, _UnsentMessages.Count);
            }

            // using this version of constructor, because RabbitMQ.Client from 3.5.x don't have ctor without cause parameter
            RunTaskSync(async () => await ShutdownAmqp(_Connection, 200 /* Constants.ReplySuccess*/, "closing target"));
            base.CloseTarget();
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
                    throw new NLogConfigurationException(string.Format("Compression type '{0}' not supported.", Compression));
            }
        }

        /// <summary>
        /// Compresses bytes using GZip data format
        /// </summary>
        /// <param name="messageBytes"></param>
        /// <returns></returns>
        private byte[] CompressMessageGZip(byte[] messageBytes)
        {
            var gzipCompressedMemStream = new MemoryStream();
            using (var gzipStream = new GZipStream(gzipCompressedMemStream, CompressionMode.Compress))
            {
                gzipStream.Write(messageBytes, 0, messageBytes.Length);
            }

            return gzipCompressedMemStream.ToArray();
        }


        /// <summary>
        /// This method wraps an async task into a synchronous call
        /// </summary>
        private void RunTaskSync(Action action)
        {
            Task.Run(action).ConfigureAwait(false).GetAwaiter().GetResult();
        }

    }
}