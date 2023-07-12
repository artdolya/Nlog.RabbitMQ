using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace Nlog.RabbitMQ.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            Logger logger = LogManager.GetLogger("RmqTarget");
            var logEventInfo = new LogEventInfo(LogLevel.Debug, "RmqLogMessage", $"This is a test message, generated at {DateTime.UtcNow}.");
            logEventInfo.Properties["fieldA"] = "Field A";
            logEventInfo.Properties["fieldB"] = "Field B";
            logEventInfo.Properties["fields"] = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("FieldC", "Field c")
            };

            // Provide global application properties
            GlobalDiagnosticsContext.Set("globalFieldA", "Global Field A");
            GlobalDiagnosticsContext.Set("globalFieldB", "Global Field B");

            // Provide context properties for the current thread
            NestedDiagnosticsLogicalContext.Push("Nested Field C");

            // Provide scope detail for the current thread
            MappedDiagnosticsLogicalContext.Set("mappedFieldA", "Mapped Field A");
            MappedDiagnosticsLogicalContext.Set("mappedFieldB", "Mapped Field B");

            logger.Log(logEventInfo);

            LogManager.Shutdown();
        }
    }
}
