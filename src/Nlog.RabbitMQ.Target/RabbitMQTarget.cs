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

		private object _sync = new object();
		private volatile bool _closed = false;

		public RabbitMQTarget()
		{
			Layout = "${message}";
			Compression = CompressionTypes.None;
			Fields = new List<Field>();

			//PrepareConnectionShutdownEventHandler();
		}

		#region Properties

		private string _VHost = "/";

		/// <summary>
		/// 	Gets or sets the virtual host to publish to.
		/// </summary>
		public string VHost
		{
			get { return _VHost; }
			set
			{
				if (value != null) _VHost = value;
			}
		}

		private string _UserName = "guest";

		/// <summary>
		/// 	Gets or sets the username to use for
		/// 	authentication with the message broker. The default
		/// 	is 'guest'
		/// </summary>
		public string UserName
		{
			get { return _UserName; }
			set { _UserName = value; }
		}

		private string _Password = "guest";

		/// <summary>
		/// 	Gets or sets the password to use for
		/// 	authentication with the message broker.
		/// 	The default is 'guest'
		/// </summary>
		public string Password
		{
			get { return _Password; }
			set { _Password = value; }
		}

		private ushort _Port = 5672;

		/// <summary>
		/// 	Gets or sets the port to use
		/// 	for connections to the message broker (this is the broker's
		/// 	listening port).
		/// 	The default is '5672'.
		/// </summary>
		public ushort Port
		{
			get { return _Port; }
			set { _Port = value; }
		}

		private Layout _Topic = "{0}";

		///<summary>
		///	Gets or sets the routing key (aka. topic) with which
		///	to send messages. Defaults to {0}, which in the end is 'error' for log.Error("..."), and
		///	so on. An example could be setting this property to 'ApplicationType.MyApp.Web.{0}'.
		///	The default is '{0}'.
		///</summary>
		public Layout Topic
		{
			get { return _Topic; }
			set { _Topic = value; }
		}

		private IProtocol _Protocol = Protocols.DefaultProtocol;

		/// <summary>
		/// 	Gets or sets the AMQP protocol (version) to use
		/// 	for communications with the RabbitMQ broker. The default 
		/// 	is the RabbitMQ.Client-library's default protocol.
		/// </summary>
		public IProtocol Protocol
		{
			get { return _Protocol; }
			set
			{
				if (value != null) _Protocol = value;
			}
		}

		private string _HostName = "localhost";

		/// <summary>
		/// 	Gets or sets the host name of the broker to log to.
		/// </summary>
		/// <remarks>
		/// 	Default is 'localhost'
		/// </remarks>
		public string HostName
		{
			get { return _HostName; }
			set
			{
				if (value != null) _HostName = value;
			}
		}

		private string _Exchange = "app-logging";

		/// <summary>
		/// 	Gets or sets the exchange to bind the logger output to.
		/// </summary>
		/// <remarks>
		/// 	Default is 'log4net-logging'
		/// </remarks>
		public string Exchange
		{
			get { return _Exchange; }
			set
			{
				if (value != null) _Exchange = value;
			}
		}

		private string _ExchangeType = "topic";

		/// <summary>
		///   Gets or sets the exchange type to bind the logger output to.
		/// </summary>
		/// <remarks>
		///   Default is 'topic'
		/// </remarks>
		public string ExchangeType
		{
			get { return _ExchangeType; }
			set
			{
				if (String.IsNullOrEmpty(value))
					return;

				_ExchangeType = value;
			}
		}

		private bool _Durable = true;

		/// <summary>
		/// 	Gets or sets the setting specifying whether the exchange
		///		is durable (persisted across restarts)
		/// </summary>
		/// <remarks>
		/// 	Default is true
		/// </remarks>
		public bool Durable
		{
			get { return _Durable; }
			set { _Durable = value; }
		}

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

		private int _MaxBuffer = 10240;

		/// <summary>
		/// Gets or sets the maximum number of messages to save in the case
		/// that the RabbitMQ instance goes down. Must be >= 1. Defaults to 10240.
		/// </summary>
		public int MaxBuffer
		{
			get { return _MaxBuffer; }
			set
			{
				if (value > 0) _MaxBuffer = value;
			}
		}

		ushort _HeartBeatSeconds = 3;

		/// <summary>
		/// Gets or sets the number of heartbeat seconds to have for the RabbitMQ connection.
		/// If the heartbeat times out, then the connection is closed (logically) and then
		/// re-opened the next time a log message comes along.
		/// </summary>
		public ushort HeartBeatSeconds
		{
			get { return _HeartBeatSeconds; }
			set { _HeartBeatSeconds = value; }
		}

		bool _UseJSON;

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
			get { return _UseJSON; }
			set { _UseJSON = value; }
		}

		bool _UseSsl;

		/// <summary>
		/// Enables SSL support to connect to the Message Queue. If this is enabled, 
		/// SslCertPath and SslCertPassphrase are required! For more information please
		/// visit http://www.rabbitmq.com/ssl.html
		/// </summary>
		public bool UseSsl
		{
			get { return _UseSsl; }
			set { _UseSsl = value; }
		}

		string _SslCertPath;

		/// <summary>
		/// Location of client SSL certificate
		/// </summary>
		public string SslCertPath
		{
			get { return _SslCertPath; }
			set { _SslCertPath = value; }
		}

		string _SslCertPassphrase;

		/// <summary>
		/// Passphrase for generated SSL certificate defined in SslCertPath
		/// </summary>
		public string SslCertPassphrase
		{
			get { return _SslCertPassphrase; }
			set { _SslCertPassphrase = value; }
		}

		DeliveryMode _DeliveryMode = DeliveryMode.NonPersistent;

		/// <summary>
		/// The delivery more, 1 for non-persistent, 2 for persistent
		/// </summary>
		public DeliveryMode DeliveryMode
		{
			get { return _DeliveryMode; }
			set { _DeliveryMode = value; }
		}

		int _Timeout = 3000;

		/// <summary>
		/// The amount of milliseconds to wait when starting a connection
		/// before moving on to next task
		/// </summary>
		public int Timeout
		{
			get { return _Timeout; }
			set { _Timeout = value; }
		}

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
			Write(new AsyncLogEventInfo(logEvent, null));
		}

		protected override void Write(AsyncLogEventInfo logEvent)
		{
			if (_closed)
			{
				return;
			}

			var continuation = logEvent.Continuation;
			var basicProperties = GetBasicProperties(logEvent.LogEvent);
			var uncompressedMessage = GetMessage(logEvent.LogEvent);
			var message = CompressMessage(uncompressedMessage);
			var routingKey = GetTopic(logEvent.LogEvent);

			if (_Model == null || !_Model.IsOpen)
				StartConnection();

			if (_Model == null || !_Model.IsOpen)
			{
				AddUnsent(routingKey, basicProperties, message);
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
				AddUnsent(routingKey, basicProperties, message);
				if (continuation == null) throw;
				continuation(e);
				InternalLogger.Error("Could not send to RabbitMQ instance! {0}", e.ToString());
			}
			catch (ObjectDisposedException e)
			{
				AddUnsent(routingKey, basicProperties, message);
				if (continuation == null) throw;
				continuation(e);
				//InternalLogger.Error("Could not write data to the network stream! {0}", e.ToString());
			}

			ShutdownAmqp(_Connection,
				new ShutdownEventArgs(ShutdownInitiator.Application, 504 /*Constants.ChannelError*/,
					"Could not talk to RabbitMQ instance", null));
			// using this version of constructor, because RabbitMQ.Client from 3.5.x don't have ctor without cause parameter
		}

		private void AddUnsent(string routingKey, IBasicProperties basicProperties, byte[] message)
		{
			if (_UnsentMessages.Count < _MaxBuffer)
				_UnsentMessages.Enqueue(Tuple.Create(message, basicProperties, routingKey));
			else
				InternalLogger.Warn("MaxBuffer {0} filled. Ignoring message.", _MaxBuffer);
		}

		private void CheckUnsent()
		{
			// using a queue so that removing and publishing is a single operation
			while (_UnsentMessages.Count > 0)
			{
				var tuple = _UnsentMessages.Dequeue();
				InternalLogger.Info("publishing unsent message: {0}.", tuple);
				Publish(tuple.Item1, tuple.Item2, tuple.Item3);
			}
		}

		private void Publish(byte[] bytes, IBasicProperties basicProperties, string routingKey)
		{
			_Model.BasicPublish(_Exchange,
				routingKey,
				true, basicProperties,
				bytes);
		}

		private string GetTopic(LogEventInfo eventInfo)
		{
			var routingKey = _Topic.Render(eventInfo);
			routingKey = routingKey.Replace("{0}", eventInfo.Level.Name);
			return routingKey;
		}

		private byte[] GetMessage(LogEventInfo info)
		{
			var msg = MessageFormatter.GetMessageInner(_UseJSON, this.UseLayoutAsMessage, Layout, info, this.Fields);
			return _Encoding.GetBytes(msg);
		}

		private IBasicProperties GetBasicProperties(LogEventInfo @event)
		{
			return new BasicProperties
			{
				ContentEncoding = "utf8",
				ContentType = _UseJSON ? "application/json" : "text/plain",
				AppId = AppId ?? @event.LoggerName,
				Timestamp = new AmqpTimestamp(MessageFormatter.GetEpochTimeStamp(@event)),
				UserId = UserName, // support Validated User-ID (see http://www.rabbitmq.com/extensions.html)
				DeliveryMode = (byte) DeliveryMode
			};
		}

		protected override void InitializeTarget()
		{
			base.InitializeTarget();

			StartConnection();
		}

		/// <summary>
		/// Never throws
		/// </summary>
		private void StartConnection()
		{
			if (_closed)
			{
				return;
			}

			lock (_sync)
			{
				var t = Task.Factory.StartNew(() =>
				{
					try
					{
						_Connection = GetConnectionFac().CreateConnection();
						AddConnectionShutdownDelegate(_Connection);

						try
						{
							_Model = _Connection.CreateModel();
						}
						catch (Exception e)
						{
							InternalLogger.Error("could not create model, {0}", e);
						}

						if (_Model != null && !Passive)
						{
							try
							{
								_Model.ExchangeDeclare(_Exchange, _ExchangeType, _Durable);
							}
							catch (Exception e)
							{
								if (_Model != null)
								{
									_Model.Dispose();
									_Model = null;
								}
								InternalLogger.Error(string.Format("could not declare exchange, {0}", e));
							}
						}
					}
					catch (Exception e)
					{
						InternalLogger.Error(string.Format("could not connect to Rabbit instance, {0}", e));
					}
				});

				if (!t.Wait(TimeSpan.FromMilliseconds(Timeout)))
					InternalLogger.Warn("starting connection-task timed out, continuing");
			}
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
			lock (_sync)
			{
				// I can't make this NOT hang when RMQ goes down
				// and then a log message is sent...

				try
				{
					if (_Model != null && _Model.IsOpen
						&& reason.ReplyCode != 504 //Constants.ChannelError
						&& reason.ReplyCode != 320 //Constants.ConnectionForced
					)
						_Model.Abort(); //_Model.Close();
				}
				catch (Exception e)
				{
					InternalLogger.Error("could not close model, {0}", e);
				}

				try
				{
					if (connection != null && connection.IsOpen)
					{
						AddConnectionShutdownDelegate(connection);
						connection.Close(reason.ReplyCode, reason.ReplyText, 1000);
						connection.Abort(1000); // you get 2 seconds to shut down!
					}
				}
				catch (Exception e)
				{
					InternalLogger.Error("could not close connection, {0}", e);
				}
			}
		}

		/// <summary>
		/// EventHandler<ShutdownEventArgs> for RabbitMQ.Client from 3.5.x version
		/// </summary>
		private void ShutdownAmqp35(object sender, ShutdownEventArgs e)
		{
			ShutdownAmqp((IConnection) sender, e);
		}

		private void AddConnectionShutdownDelegate(IConnection connection)
		{
			connection.ConnectionShutdown += (o, a) => ShutdownAmqp(connection, a);
		}

		#endregion

		// Dispose calls CloseTarget!
		protected override void CloseTarget()
		{
			_closed = true;
			ShutdownAmqp(_Connection,
				new ShutdownEventArgs(ShutdownInitiator.Application, 200 /* Constants.ReplySuccess*/, "closing appender", null));
			// using this version of constructor, because RabbitMQ.Client from 3.5.x don't have ctor without cause parameter

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