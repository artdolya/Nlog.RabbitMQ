using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using NLog;

namespace Nlog.RabbitMQ.Target
{
    public static class MessageFormatter
    {
        private static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);
        private static readonly IDictionary<string, object> EmptyDictionary = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());
        private static readonly ICollection<string> EmptyHashSet = Array.Empty<string>();

        public static string GetMessageInner(JsonSerializer jsonSerializer, string message, string messageSource, LogEventInfo logEvent, IList<Field> fields, ICollection<KeyValuePair<string, object>> contextProperties)
        {
            var logLine = new LogLine
            {
                TimeStampISO8601 = logEvent.TimeStamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Message = message,
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

            if (contextProperties?.Count > 0)
            {
                foreach (var p in contextProperties)
                    logLine.AddField(p.Key, p.Value);
            }

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
            jsonSerializerSettings.Converters.Add(new JsonToStringConverter(typeof(System.IO.Stream)));
            jsonSerializerSettings.Error = (sender, args) =>
            {
                NLog.Common.InternalLogger.Debug(args.ErrorContext.Error, "RabbitMQ: Error serializing property '{0}', property ignored", args.ErrorContext.Member);
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
