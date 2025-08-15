using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Nlog.RabbitMQ.Target
{
    public class MessageFormatter
    {
        private readonly JsonSerializerSettings _jsonOptions;

        public MessageFormatter()
        {
            _jsonOptions = CreateJsonSerializerSettings();
        }

        public string GetMessage(string message, string messageSource, LogEventInfo logEvent, IList<Field> fields, ICollection<KeyValuePair<string, object>> contextProperties)
        {
            var logLine = new LogLine
            {
                TimeStampISO8601 = logEvent.TimeStamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Message = message,
                Level = logEvent.Level.Name,
                Type = "amqp",
                Source = messageSource,
            };

            if (logEvent.Exception != null)
            {
                logLine.AddField("exception", logEvent.Exception.ToString());
            }

            if (logEvent.HasProperties)
            {
                foreach (var propertyPair in logEvent.Properties)
                {
                    var key = propertyPair.Key as string;
                    if (string.IsNullOrEmpty(key))
                        continue;

                    var value = propertyPair.Value;

                    if (key == "tags" && value is IEnumerable<string> tags)
                    {
                        foreach (var tag in tags)
                            logLine.AddTag(tag);
                        continue;
                    }
                    else if (key == "fields" && value is IEnumerable<KeyValuePair<string, object>> bonusFields)
                    {
                        foreach (var kv in bonusFields)
                        {
                            var v = kv.Value;
                            logLine.AddField(kv.Key, (v is string || v is ValueType) ? v : v?.ToString());
                        }
                        continue;
                    }

                    logLine.AddField(key, (value is string || value is ValueType) ? value : value?.ToString());
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

            logLine.Fields ??= new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

            logLine.Tags ??= Array.Empty<string>();

            string serializedJson = JsonConvert.SerializeObject(logLine, typeof(LogLine), _jsonOptions);
            return serializedJson;
        }

        private JsonSerializerSettings CreateJsonSerializerSettings()
        {
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };
            jsonSerializerSettings.Converters.Add(
                new StringEnumConverter()
                );

            return jsonSerializerSettings;
        }
    }
}
