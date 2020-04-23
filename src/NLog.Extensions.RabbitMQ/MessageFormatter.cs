using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using NLog;
using NLog.Layouts;

namespace NLog.Extensions.RabbitMQ
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

		public static string GetMessageInner(bool useJSON, bool useLayoutAsMessage, Layout layout, LogEventInfo logEvent, IList<Field> fields)
		{
			if (!useJSON)
				return layout.Render(logEvent);

			var logLine = new LogLine
			{
				TimeStampISO8601 = logEvent.TimeStamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
				Message = useLayoutAsMessage ? layout.Render(logEvent) : logEvent.FormattedMessage,
				Level = logEvent.Level.Name,
				Type = "amqp",
				Source = new Uri(string.Format("nlog://{0}/{1}", HostName, logEvent.LoggerName))
			};

			logLine.AddField("exception", logEvent.Exception);

            if (logEvent.HasProperties)
            {
                if (logEvent.Properties.ContainsKey("fields"))
                {
                    foreach (var kv in (IEnumerable<KeyValuePair<string, object>>)logEvent.Properties["fields"])
                        logLine.AddField(kv.Key, kv.Value);
                }

                if (logEvent.Properties.ContainsKey("tags"))
                {
                    foreach (var tag in (IEnumerable<string>)logEvent.Properties["tags"])
                        logLine.AddTag(tag);
                }

                foreach (var propertyPair in logEvent.Properties)
                {
                    var key = propertyPair.Key as string;
                    if (key == null || key == "tags" || key == "fields")
                        continue;

                    logLine.AddField(key, propertyPair.Value);
                }
            }

			if (fields != null)
				foreach (Field field in fields)
					logLine.AddField(field.Key, field.Name, field.Layout.Render(logEvent));

			logLine.EnsureADT();

			return JsonConvert.SerializeObject(logLine,
                new JsonSerializerSettings
                {
					ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                });
		}

		public static long GetEpochTimeStamp(LogEventInfo @event)
		{
			return Convert.ToInt64((@event.TimeStamp - _epoch).TotalSeconds);
		}
	}
}
