using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Nlog.RabbitMQ.Target
{
    public class LogLine
    {
        [JsonProperty("@source")]
        public string Source { get; set; }

        [JsonProperty("@timestamp")]
        public string TimeStampISO8601 { get; set; }

        [JsonProperty("@message")]
        public string Message { get; set; }

        [JsonProperty("@fields")]
        public IDictionary<string, object> Fields { get; set; }

        [JsonProperty("@tags")]
        public ICollection<string> Tags { get; set; }

        [JsonProperty("@type")]
        public string Type { get; set; }

        [JsonProperty("level")]
        public string Level { get; set; }
    }

    public static class LogLineEx
    {
        public static void AddField(
            this LogLine line, string key,
            string name, object value)
        {
            if (line.Fields == null)
                line.Fields = new Dictionary<string, object>();

            if (line.Fields.ContainsKey(key))
            {
                line.Fields.Remove(key);
            }

            if (string.IsNullOrEmpty(name) || value == null)
                return;

            line.AddField(name, value);
        }

        public static void AddField(
            this LogLine line,
            string name, object value)
        {
            if (string.IsNullOrEmpty(name) || value == null)
                return;

            if (line.Fields == null)
                line.Fields = new Dictionary<string, object>();

            line.Fields[name] = value;
        }

        public static void AddTag(this LogLine line, string tag)
        {
            if (tag == null)
                return;

            if (line.Tags == null)
                line.Tags = new HashSet<string> { tag };
            else
                line.Tags.Add(tag);
        }
    }
}