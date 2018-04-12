using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using RabbitMQ.Client;
using RabbitMQ.Client.Framing;

namespace Nlog.RabbitMQ.Target
{
	/// <summary>
	/// TODO
	/// </summary>
	[Target("RabbitMQ")]
	public class RabbitMQTarget : TargetWithLayout
	{
		public enum CompressionTypes
		{
			None,
			GZip
		};

		private IConnection _Connection;
		private IModel _Model;
		private readonly Encoding _Encoding = Encoding.UTF8;

		private readonly Queue<Tuple<byte[], IBasicProperties, string>> _UnsentMessages
			= new Queue<Tuple<byte[], IBasicProperties, string>>(512);

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
		public string VHost { get; set; } = "/";

		/// <summary>
		/// 	Gets or sets the username to use for
		/// 	authentication with the message broker. The default
		/// 	is 'guest'
		/// </summary>
		public string UserName { get; set; } = "guest";

		/// <summary>
		/// 	Gets or sets the password to use for
		/// 	authentication with the message broker.
		/// 	The default is 'guest'
		/// </summary>
		public string Password { get; set; } = "guest";

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
		public Layout Topic { get; set; } = "{0}";

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
		public string HostName { get; set; } = "localhost";

		/// <summary>
		/// 	Gets or sets the exchange to bind the logger output to.
		/// </summary>
		/// <remarks>
		/// 	Default is 'app-logging'
		/// </remarks>
		public string Exchange { get; set; } = "app-logging";

		/// <summary>
		///   Gets or sets the exchange type to bind the logger output to.
		/// </summary>
		/// <remarks>
		///   Default is 'topic'
		/// </remarks>
		public string ExchangeType { get; set; } = "topic";

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
		/// 	Gets or sets the application id to specify when sending. Defaults to null,
		/// 	and then IBasicProperties.AppId will be the name of the logger instead.
		/// </summary>
		public string AppId { get; set; }

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
		public string SslCertPath { get; set; }

		/// <summary>
		/// Passphrase for generated SSL certificate defined in SslCertPath
		/// </summary>
		public string SslCertPassphrase { get; set; }

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
			var basicProperties = GetBasicProperties(logEvent);
			var uncompressedMessage = GetMessage(logEvent);
			var message = CompressMessage(uncompressedMessage);
			var routingKey = GetTopic(logEvent);

			if (_Model == null || !_Model.IsOpen)
				StartConnection(true);

			if (_Model == null || !_Model.IsOpen)
			{
				if (!AddUnsent(routingKey, basicProperties, message))
				{
					throw new InvalidOperationException("LogEvent discarded because RabbitMQ instance is offline and reached MaxBuffer");
				}
				return;
			}

			try
			{
				CheckUnsent();
				Publish(message, basicProperties, routingKey);
				return;
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

			// using this version of constructor, because RabbitMQ.Client from 3.5.x don't have ctor without cause parameter
			var shutdownEvenArgs = new ShutdownEventArgs(ShutdownInitiator.Application, 504 /*Constants.ChannelError*/,
					"Could not talk to RabbitMQ instance", null);
			ShutdownAmqp(_Connection, shutdownEvenArgs);
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

		private void CheckUnsent()
		{
			// using a queue so that removing and publishing is a single operation
			while (_UnsentMessages.Count > 0)
			{
				var tuple = _UnsentMessages.Dequeue();
				InternalLogger.Info("RabbitMQTarget(Name={0}): Publishing unsent message: {1}.", Name, tuple);
				Publish(tuple.Item1, tuple.Item2, tuple.Item3);
			}
		}

		private void Publish(byte[] bytes, IBasicProperties basicProperties, string routingKey)
		{
			_Model.BasicPublish(Exchange,
				routingKey,
				true, basicProperties,
				bytes);
		}

		private string GetTopic(LogEventInfo eventInfo)
		{
			var routingKey = Topic.Render(eventInfo);
			routingKey = routingKey.Replace("{0}", eventInfo.Level.Name);
			return routingKey;
		}

		private byte[] GetMessage(LogEventInfo info)
		{
			var msg = MessageFormatter.GetMessageInner(UseJSON, this.UseLayoutAsMessage, Layout, info, this.Fields);
			return _Encoding.GetBytes(msg);
		}

		private IBasicProperties GetBasicProperties(LogEventInfo @event)
		{
			return new BasicProperties
			{
				ContentEncoding = "utf8",
				ContentType = (UseJSON || Layout is JsonLayout) ? "application/json" : "text/plain",
				AppId = AppId ?? @event.LoggerName,
				Timestamp = new AmqpTimestamp(MessageFormatter.GetEpochTimeStamp(@event)),
				UserId = UserName, // support Validated User-ID (see http://www.rabbitmq.com/extensions.html)
				DeliveryMode = (byte)DeliveryMode
			};
		}

		protected override void InitializeTarget()
		{
			base.InitializeTarget();
			StartConnection(false);
		}

		/// <summary>
		/// Never throws
		/// </summary>
		private void StartConnection(bool checkInitialized)
		{
			if (_Model != null)
			{
				if (_Model.IsOpen)
					return;

				var shutdownEvenArgs = new ShutdownEventArgs(ShutdownInitiator.Application, 504 /*Constants.ChannelError*/,
						"Model not open to RabbitMQ instance", null);
				ShutdownAmqp(_Connection, shutdownEvenArgs);
			}

			var t = Task.Factory.StartNew(() =>
			{
				if (checkInitialized && !IsInitialized)
					return;

				if (_Model != null && _Model.IsOpen)
					return;

				IModel model = null;
				IConnection connection = null;

				try
				{
					connection = GetConnectionFac().CreateConnection();
					connection.ConnectionShutdown += (s, e) => ShutdownAmqp(connection, e);

					try
					{
						model = connection.CreateModel();
					}
					catch (Exception e)
					{
						InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not create model, {1}", Name, e.Message);
						var shutdownConnection = connection;
						connection = null;
						shutdownConnection.Close(1000);
						shutdownConnection.Abort(1000);
					}

					if (model != null && !Passive)
					{
						try
						{
							model.ExchangeDeclare(Exchange, ExchangeType, Durable);
						}
						catch (Exception e)
						{
							InternalLogger.Error(e, string.Format("RabbitMQTarget(Name={0}): Could not declare exchange: {1}", Name, e.Message));
							var shutdownConnection = connection;
							connection = null;
							model.Dispose();
							model = null;
							shutdownConnection.Close(1000);
							shutdownConnection.Abort(1000);
						}
					}
				}
				catch (Exception e)
				{
					InternalLogger.Error(e, string.Format("RabbitMQTarget(Name={0}): Could not connect to Rabbit instance: {1}", Name, e.Message));
				}
				finally
				{
					if (connection != null && model != null)
					{
						lock (SyncRoot)
						{
							if (_Model == null || !_Model.IsOpen)
							{
								_Connection = connection;
								_Model = model;
							}
						}
					}
				}
			});

			if (!t.Wait(TimeSpan.FromMilliseconds(Timeout)))
				InternalLogger.Warn("RabbitMQTarget(Name={0}): Starting connection-task timed out, continuing", Name);
		}

		private ConnectionFactory GetConnectionFac()
		{
			return new ConnectionFactory
			{
				HostName = HostName,
				VirtualHost = VHost,
				UserName = UserName,
				Password = Password,
				RequestedHeartbeat = HeartBeatSeconds,
				Port = Port,
				Ssl = new SslOption()
				{
					Enabled = UseSsl,
					CertPath = SslCertPath,
					CertPassphrase = SslCertPassphrase,
					ServerName = HostName
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

			lock (SyncRoot)
			{
				if (connection != null && ReferenceEquals(connection, _Connection))
				{
					var model = _Model;

					_Connection = null;
					_Model = null;

					try
					{
						if (model != null && model.IsOpen
							&& reason.ReplyCode != 504 //Constants.ChannelError
							&& reason.ReplyCode != 320 //Constants.ConnectionForced
						)
							model.Abort(); //model.Close();
					}
					catch (Exception e)
					{
						InternalLogger.Error(e, "RabbitMQTarget(Name={0}): Could not close model: {1}", Name, e.Message);
					}

					try
					{
						if (connection.IsOpen)
						{
							connection.Close(reason.ReplyCode, reason.ReplyText, 1000);
							connection.Abort(1000); // you get 2 seconds to shut down!
						}
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