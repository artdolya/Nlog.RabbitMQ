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

			logger.Log(logEventInfo);
		}
	}
}
