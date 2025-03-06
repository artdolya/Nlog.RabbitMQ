using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using NLog.Layouts;

namespace NLog.Targets.RabbitMq
{
    public class MessageFormatter
    {
        private readonly JsonSerializerOptions _jsonOptions;

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
                logLine.Fields = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

            if (logLine.Tags == null)
                logLine.Tags = Array.Empty<string>();

            var serializedJson = JsonSerializer.Serialize(logLine, typeof(LogLine), _jsonOptions);
            return  serializedJson;
        }

        private JsonSerializerOptions CreateJsonSerializerSettings()
        {
            var jsonSerializerSettings = new JsonSerializerOptions { ReferenceHandler  = ReferenceHandler.IgnoreCycles };
            jsonSerializerSettings.Converters.Add(new JsonStringEnumConverter());
            return jsonSerializerSettings;
        }
    }
}
