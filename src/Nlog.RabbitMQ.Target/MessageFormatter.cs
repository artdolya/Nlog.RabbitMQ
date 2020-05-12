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

        public static string GetMessageInner(bool useJSON, bool addGdc, bool addNdlc, bool addMdlc, Layout layout, LogEventInfo info, IList<Field> fields)
        {
            return GetMessageInner(useJSON, false, addGdc, addNdlc, addMdlc, layout, info, fields);
        }

        public static string GetMessageInner(bool useJSON,bool useLayoutAsMessage, bool addGdc, bool addNdlc, bool addMdlc, Layout layout, LogEventInfo logEvent, IList<Field> fields)
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

            if (addGdc)
            {
                foreach (var gdc in GlobalDiagnosticsContext.GetNames())
                {
                    logLine.AddField(gdc, GlobalDiagnosticsContext.GetObject(gdc));
                }
            }

            if (addNdlc)
            {
                foreach (var ndlc in NestedDiagnosticsLogicalContext.GetAllObjects())
                {
                    logLine.AddField("nested", ndlc);
                }
            }

            if (addMdlc)
            {
                foreach (var mdlc in MappedDiagnosticsLogicalContext.GetNames())
                {
                    logLine.AddField(mdlc, MappedDiagnosticsLogicalContext.GetObject(mdlc));
                }
            }

            if (fields != null)
				foreach (Field field in fields)
					logLine.AddField(field.Key, field.Name, field.Layout.Render(logEvent));

			logLine.EnsureADT();

			return JsonConvert.SerializeObject(logLine);
		}

		public static long GetEpochTimeStamp(LogEventInfo @event)
		{
			return Convert.ToInt64((@event.TimeStamp - _epoch).TotalSeconds);
		}
	}
}
