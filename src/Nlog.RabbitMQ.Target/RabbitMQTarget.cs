using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using RabbitMQ.Client;

namespace Nlog.RabbitMQ.Target
{
	/// <summary>
	/// TODO
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
		private IModel _Model;
		private string _ModelExchange;
		private readonly Encoding _Encoding = Encoding.UTF8;

		private readonly Queue<Tuple<byte[], IBasicProperties, string>> _UnsentMessages
			= new Queue<Tuple<byte[], IBasicProperties, string>>(512);

		private readonly object _sync = new object();

		private JsonSerializer JsonSerializer => _jsonSerializer ?? (_jsonSerializer = JsonSerializer.Create(MessageFormatter.CreateJsonSerializerSettings()));
		private JsonSerializer _jsonSerializer;

		public RabbitMQTarget()
		{
			Layout = "${message}";
			Compression = CompressionTypes.None;
			Fields = new List<Field>();
		}

		#region Properties

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
		/// 	The default is '5672'.
		/// </summary>
		public ushort Port { get; set; } = 5672;

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
		//		if RabbitMQ server supports it. This value doesn't have to be unique and cannot
		//		be used as a connection identifier, e.g. in HTTP API requests. This value is
		//		supposed to be human-readable.
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
		public Layout MessageSource { get; set; } = "nlog://${machinename}/${logger}";

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
		public bool UseJSON { get; set; }

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
		public bool UseLayoutAsMessage { get; set; }

		#endregion

		protected override void Write(LogEventInfo logEvent)
		{
			var uncompressedMessage = GetMessage(logEvent);
			var message = CompressMessage(uncompressedMessage);

			var routingKey = RenderLogEvent(Topic,  logEvent);
			if (routingKey.IndexOf("{0}") >= 0)
				routingKey = routingKey.Replace("{0}", logEvent.Level.Name);

			var model = _Model;
			var modelExchange = _ModelExchange;
			if (model == null || !model.IsOpen)
			{
				StartConnection(_Connection, Timeout, true);
				model = _Model;
				modelExchange = _ModelExchange;
			}

			var basicProperties = GetBasicProperties(logEvent, model);

			if (model == null || !model.IsOpen)
			{
				if (!AddUnsent(routingKey, basicProperties, message))
				{
					throw new InvalidOperationException("LogEvent discarded because RabbitMQ instance is offline and reached MaxBuffer");
				}
				return;
			}

			bool restartConnection = true;

			try
			{
				CheckUnsent(model, modelExchange);
				Publish(model, message, basicProperties, routingKey, modelExchange);
				restartConnection = false;
			}
			catch (IOException e)
			{
				InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not send to RabbitMQ instance: {1}", Name, e.Message);
				if (!AddUnsent(routingKey, basicProperties, message))
					throw;
			}
			catch (ObjectDisposedException e)
			{
				InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not send to RabbitMQ instance: {1}", Name, e.Message);
				if (!AddUnsent(routingKey, basicProperties, message))
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

		private bool AddUnsent(string routingKey, IBasicProperties basicProperties, byte[] message)
		{
			if (_UnsentMessages.Count < MaxBuffer)
			{
				_UnsentMessages.Enqueue(Tuple.Create(message, basicProperties, routingKey));
				return true;
			}
			else
			{
				InternalLogger.Warn("RabbitMQTarget(Name={0}): MaxBuffer {1} filled. Ignoring message.", Name, MaxBuffer);
				return false;
			}
		}

		private void CheckUnsent(IModel model, string exchange)
		{
			// using a queue so that removing and publishing is a single operation
			while (_UnsentMessages.Count > 0)
			{
				var tuple = _UnsentMessages.Dequeue();
				InternalLogger.Info("RabbitMQTarget(Name={0}): Publishing unsent message: {1}.", Name, tuple);
				Publish(model, tuple.Item1, tuple.Item2, tuple.Item3, exchange);
			}
		}

		private void Publish(IModel model, byte[] bytes, IBasicProperties basicProperties, string routingKey, string exchange)
		{
			model.BasicPublish(exchange,
				routingKey,
				true, basicProperties,
				bytes);
		}

		private byte[] GetMessage(LogEventInfo logEvent)
		{
			var msg = GetMessageString(logEvent);
			return _Encoding.GetBytes(msg);
		}

		private string GetMessageString(LogEventInfo logEvent)
		{
			if (!UseJSON)
			{
				return RenderLogEvent(Layout, logEvent);
			}
			else
			{
				var message = this.UseLayoutAsMessage ? RenderLogEvent(Layout, logEvent) : logEvent.FormattedMessage;
				var messageSource = RenderLogEvent(MessageSource, logEvent);
				if (string.IsNullOrEmpty(messageSource))
					messageSource = string.Format("nlog://{0}/{1}", System.Net.Dns.GetHostName(), logEvent.LoggerName);

				try
				{
					var jsonSerializer = JsonSerializer;
					lock (jsonSerializer)
                    {
						return MessageFormatter.GetMessageInner(jsonSerializer, message, messageSource, logEvent, Fields, GetFullContextProperties(logEvent));
					}
				}
				catch (Exception e)
				{
					_jsonSerializer = null;	// reset as it it might be broken
					InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Failed to serialize LogEvent: {1}", Name, e.Message);
					throw;
				}
			}
		}

        private IDictionary<string, object> GetFullContextProperties(LogEventInfo logEvent)
        {
            var allProperties = GetContextProperties(logEvent) ?? new Dictionary<string, object>();
            var ndlcProperties = GetContextNdlc(logEvent);
            if (IncludeNdlc && ndlcProperties.Count > 0)
                allProperties.Add("nested", ndlcProperties);

            return allProperties;
        }

		private IBasicProperties GetBasicProperties(LogEventInfo logEvent, IModel model)
		{
			var basicProperties = model.CreateBasicProperties();
			basicProperties.ContentEncoding = "utf8";
			basicProperties.ContentType = (UseJSON || Layout is JsonLayout) ? "application/json" : "text/plain";
			basicProperties.AppId = RenderLogEvent(AppId, logEvent);
			if (string.IsNullOrEmpty(basicProperties.AppId))
				basicProperties.AppId = logEvent.LoggerName;
			basicProperties.Timestamp = new AmqpTimestamp(MessageFormatter.GetEpochTimeStamp(logEvent));
			basicProperties.UserId = RenderLogEvent(UserName, logEvent);   // support Validated User-ID (see http://www.rabbitmq.com/extensions.html)
			basicProperties.DeliveryMode = (byte)DeliveryMode;
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

		/// <summary>
		/// Never throws
		/// </summary>
		private void StartConnection(IConnection oldConnection, int timeoutMilliseconds, bool checkInitialized)
		{
			if (!ReferenceEquals(oldConnection, _Connection) && (_Model?.IsOpen ?? false))
				return;

			var t = Task.Run(() =>
			{
				// Do not lock on SyncRoot, as we are waiting on this task while holding SyncRoot-lock
				lock (_sync)
				{
					if (checkInitialized && !IsInitialized)
						return;

					if (!ReferenceEquals(oldConnection, _Connection) && (_Model?.IsOpen ?? false))
						return;

					InternalLogger.Info("RabbitMQTarget(Name={0}): Connection attempt started...", Name);
					oldConnection = _Connection ?? oldConnection;
					if (oldConnection != null)
					{
						var shutdownEvenArgs = new ShutdownEventArgs(ShutdownInitiator.Application, 504 /*Constants.ChannelError*/,
	"Model not open to RabbitMQ instance", null);
						ShutdownAmqp(oldConnection, shutdownEvenArgs);
					}

					IModel model = null;
					IConnection connection = null;
					string exchange = null;

					try
					{
						var factory = GetConnectionFac(out exchange, out var exchangeType, out var clientProvidedName);
						connection = string.IsNullOrEmpty(clientProvidedName) ? factory.CreateConnection() : factory.CreateConnection(clientProvidedName);
						connection.ConnectionShutdown += (s, e) => ShutdownAmqp(ReferenceEquals(_Connection, connection) ? connection : null, e);

						try
						{
							model = connection.CreateModel();
						}
						catch (Exception e)
						{
							InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not create model, {1}", Name, e.Message);
							throw;
						}

						if (model != null && !Passive)
						{
							try
							{
								model.ExchangeDeclare(exchange, exchangeType, Durable);
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
							InternalLogger.Error(e, string.Format("RabbitMQTarget(Name={0}): Could not connect to Rabbit instance: {1}", Name, e.Message));
						}
						else
						{
							model?.Dispose();
							model = null;
							shutdownConnection.Close(TimeSpan.FromMilliseconds(1000));
							shutdownConnection.Abort(TimeSpan.FromMilliseconds(1000));
						}
					}
					finally
					{
						if (connection != null && model != null)
						{
							if ((_Model?.IsOpen ?? false))
							{
								InternalLogger.Info("RabbitMQTarget(Name={0}): Connection attempt completed succesfully, but not needed", Name);
							}
							else
							{
								_Connection = connection;
								_Model = model;
								_ModelExchange = exchange;
								InternalLogger.Info("RabbitMQTarget(Name={0}): Connection attempt completed succesfully", Name);
							}
						}
					}
				}
			});

			var completedTask = Task.WhenAny(t, Task.Delay(TimeSpan.FromMilliseconds(timeoutMilliseconds))).ConfigureAwait(false).GetAwaiter().GetResult();
			if (!ReferenceEquals(completedTask, t))
			{
				InternalLogger.Warn("RabbitMQTarget(Name={0}): Starting connection-task timed out, continuing", Name);
			}
			else if (completedTask.Exception != null)
			{
				InternalLogger.Error(completedTask.Exception, "RabbitMQTarget(Name={0}): Starting connection-task failed: {0}", Name, completedTask.Exception.Message);
			}
		}

		private ConnectionFactory GetConnectionFac(out string exchange, out string exchangeType, out string clientProvidedName)
		{
			var nullLogEvent = LogEventInfo.CreateNullEvent();
			var hostName = RenderLogEvent(HostName, nullLogEvent);
			var vHost = RenderLogEvent(VHost, nullLogEvent);
			exchange = RenderLogEvent(Exchange, nullLogEvent);
			exchangeType = RenderLogEvent(ExchangeType, nullLogEvent);
			clientProvidedName = RenderLogEvent(ClientProvidedName, nullLogEvent);
			var userName = RenderLogEvent(UserName, nullLogEvent);
			var password = RenderLogEvent(Password, nullLogEvent);
			var sslCertPath = RenderLogEvent(SslCertPath, nullLogEvent);
			var sslCertPassphrase = RenderLogEvent(SslCertPassphrase, nullLogEvent);

			return new ConnectionFactory
			{
				HostName = hostName,
				VirtualHost = vHost,
				UserName = userName,
				Password = password,
				RequestedHeartbeat = TimeSpan.FromSeconds(HeartBeatSeconds),
				Port = Port,
				Ssl = new SslOption()
				{
					Enabled = UseSsl,
					CertPath = sslCertPath,
					CertPassphrase = sslCertPassphrase,
					ServerName = hostName
				}
			};
		}

		#region ConnectionShutdownEventHandler

		private void ShutdownAmqp(IConnection connection, ShutdownEventArgs reason)
		{
			if (reason.ReplyCode != 200 /* Constants.ReplySuccess*/)
			{
				InternalLogger.Warn("RabbitMQTarget(Name={0}): Connection shutdown. ReplyCode={1}, ReplyText={2}", Name, reason.ReplyCode, reason.ReplyText);
			}
			else
			{
				InternalLogger.Info("RabbitMQTarget(Name={0}): Connection shutdown. ReplyCode={1}, ReplyText={2}", Name, reason.ReplyCode, reason.ReplyText);
			}

			lock (_sync)
			{
				if (connection != null)
				{
					IModel model = null;

					if (ReferenceEquals(connection, _Connection))
					{
						model = _Model;
						_Connection = null;
						_Model = null;
					}

					try
					{
						if (reason.ReplyCode == 200 /* Constants.ReplySuccess*/ && connection.IsOpen)
							model?.Close();
						else
							model?.Abort(); // Abort is close without throwing Exceptions
					}
					catch (Exception e)
					{
						InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not close model: {1}", Name, e.Message);
					}

					try
					{
						// you get 1.5 seconds to shut down!
						if (reason.ReplyCode == 200 /* Constants.ReplySuccess*/ && connection.IsOpen)
							connection.Close(reason.ReplyCode, reason.ReplyText, TimeSpan.FromMilliseconds(1500)); 
						else
							connection.Abort(reason.ReplyCode, reason.ReplyText, TimeSpan.FromMilliseconds(1500));
					}
					catch (Exception e)
					{
						InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not close connection: {1}", Name, e.Message);
					}
				}
			}
		}

		#endregion

		// Dispose calls CloseTarget!
		protected override void CloseTarget()
		{
			// using this version of constructor, because RabbitMQ.Client from 3.5.x don't have ctor without cause parameter
			var shutdownEventArgs = new ShutdownEventArgs(ShutdownInitiator.Application, 200 /* Constants.ReplySuccess*/, "closing target", null);
			ShutdownAmqp(_Connection, shutdownEventArgs);
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

	}
}