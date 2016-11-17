using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using NLog;
using NLog.Layouts;

namespace Nlog.RabbitMQ.Target
{
	public static class MessageFormatter
	{
		private static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);

		static string _hostName;
		private static string HostName
		{
			get { return _hostName = (_hostName ?? Dns.GetHostName()); }
		}

		public static string GetMessageInner(bool useJSON, Layout layout, LogEventInfo info, IList<Field> fields)
		{
			return GetMessageInner(useJSON, false, layout, info, fields);
		}

		public static string GetMessageInner(bool useJSON, bool useLayoutAsMessage, Layout layout, LogEventInfo info, IList<Field> fields)
		{
			if (!useJSON)
				return layout.Render(info);

			var logLine = new LogLine
			{
				TimeStampISO8601 = info.TimeStamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
				Message = useLayoutAsMessage ? layout.Render(info) : info.FormattedMessage,
				Level = info.Level.Name,
				Type = "amqp",
				Source = new Uri(string.Format("nlog://{0}/{1}", HostName, info.LoggerName))
			};

			logLine.AddField("exception", info.Exception);

			if (info.Properties.Count > 0 && info.Properties.ContainsKey("fields"))
				foreach (var kv in (IEnumerable<KeyValuePair<string, object>>)info.Properties["fields"])
					logLine.AddField(kv.Key, kv.Value);

			if (info.Properties.Count > 0 && info.Properties.ContainsKey("tags"))
				foreach (var tag in (IEnumerable<string>)info.Properties["tags"])
					logLine.AddTag(tag);

			foreach (var propertyPair in info.Properties)
			{
				var key = propertyPair.Key as string;
				if (key == null || key == "tags" || key == "fields")
					continue;

				logLine.AddField((string)propertyPair.Key, propertyPair.Value);
			}

			if (fields != null)
				foreach (Field field in fields)
					logLine.AddField(field.Key, field.Name, field.Layout.Render(info));

			logLine.EnsureADT();

			return JsonConvert.SerializeObject(logLine);
		}

		public static long GetEpochTimeStamp(LogEventInfo @event)
		{
			return Convert.ToInt64((@event.TimeStamp - _epoch).TotalSeconds);
		}
	}
}
