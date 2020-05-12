using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Newtonsoft.Json;
using NLog;

namespace Nlog.RabbitMQ.Target
{
    public static class MessageFormatter
    {
        private static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);
        private static readonly IDictionary<string, object> EmptyDictionary = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());
        private static readonly ICollection<string> EmptyHashSet = Array.Empty<string>();

        public static string GetMessageInner(JsonSerializer jsonSerializer, bool addGdc, bool addNdlc, bool addMdlc, string mesage, string messageSource, LogEventInfo logEvent, IList<Field> fields)
        {
            var logLine = new LogLine
            {
                TimeStampISO8601 = logEvent.TimeStamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Message = mesage,
                Level = logEvent.Level.Name,
                Type = "amqp",
                Source = messageSource,
            };

            logLine.AddField("exception", logEvent.Exception);

            if (logEvent.HasProperties)
            {
                foreach (var propertyPair in logEvent.Properties)
                {
                    var key = propertyPair.Key as string;
                    if (string.IsNullOrEmpty(key))
                        continue;

                    if (key == "tags" && propertyPair.Value is IEnumerable<string> tags)
                    {
                        foreach (var tag in tags)
                            logLine.AddTag(tag);
                    }
                    else if (key == "fields" && propertyPair.Value is IEnumerable<KeyValuePair<string, object>> bonusFields)
                    {
                        foreach (var kv in bonusFields)
                            logLine.AddField(kv.Key, kv.Value);
                    }

                    logLine.AddField(key, propertyPair.Value);
                }
            }
			
            if (addGdc)
                foreach (var gdc in GlobalDiagnosticsContext.GetNames())
                    logLine.AddField(gdc, GlobalDiagnosticsContext.GetObject(gdc));

            if (addNdlc)
                foreach (var ndlc in NestedDiagnosticsLogicalContext.GetAllObjects())
                    logLine.AddField("nested", ndlc);

            if (addMdlc)
                foreach (var mdlc in MappedDiagnosticsLogicalContext.GetNames())
                    logLine.AddField(mdlc, MappedDiagnosticsLogicalContext.GetObject(mdlc));


            if (fields?.Count > 0)
            {
                foreach (Field field in fields)
                    logLine.AddField(field.Key, field.Name, field.Layout.Render(logEvent));
            }

            if (logLine.Fields == null)
                logLine.Fields = EmptyDictionary;

            if (logLine.Tags == null)
                logLine.Tags = EmptyHashSet;

            var sb = new System.Text.StringBuilder(256);
            var sw = new System.IO.StringWriter(sb, CultureInfo.InvariantCulture);
            using (JsonTextWriter jsonWriter = new JsonTextWriter(sw))
            {
                jsonWriter.Formatting = jsonSerializer.Formatting;
                jsonSerializer.Serialize(jsonWriter, logLine, typeof(LogLine));
            }
            return sb.ToString();
        }

        public static JsonSerializerSettings CreateJsonSerializerSettings()
        {
            var jsonSerializerSettings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            jsonSerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            jsonSerializerSettings.Converters.Add(new JsonToStringConverter(typeof(System.Reflection.MemberInfo)));
            jsonSerializerSettings.Converters.Add(new JsonToStringConverter(typeof(System.Reflection.Assembly)));
            jsonSerializerSettings.Converters.Add(new JsonToStringConverter(typeof(System.Reflection.Module)));
            jsonSerializerSettings.Error = (sender, args) =>
            {
                args.ErrorContext.Handled = true;
            };
            return jsonSerializerSettings;
        }

        public static long GetEpochTimeStamp(LogEventInfo @event)
        {
            return Convert.ToInt64((@event.TimeStamp - _epoch).TotalSeconds);
        }
    }
}
