# Nlog.RabbitMQ.Target

RabbitMQ Target for popular NLog logging tool

[![NuGet](https://img.shields.io/nuget/v/Nlog.RabbitMQ.Target.svg)](https://www.nuget.org/packages/Nlog.RabbitMQ.Target/)
[![master](https://github.com/artdolya/Nlog.RabbitMQ/actions/workflows/bump.yml/badge.svg)](https://github.com/artdolya/Nlog.RabbitMQ/actions/workflows/bump.yml)

## 🚀 Async RabbitMQ Target

**New in vNEXT:** The RabbitMQ target is now fully asynchronous! Logging to RabbitMQ no longer blocks your application threads, resulting in improved performance and reliability, especially under high load or when the broker is slow/unavailable.

### Benefits

-   Non-blocking logging: your app remains responsive even if RabbitMQ is slow or down.
-   Improved throughput and scalability.
-   More robust error handling and buffering.

### Migration Notes

-   The target itself is now async; you do **not** need to wrap it in `<targets async="true">` (though you still can for extra safety).
-   Buffering and message discarding now happen inside the RabbitMQ target. See `maxBuffer` for configuration.
-   If you previously relied on the sync behavior, review your error handling and shutdown logic.
-   No breaking changes to configuration, but you may want to tune `maxBuffer` and `Timeout` for your workload.

### Example Configuration

```xml
<targets>
  <target name="RabbitMqTarget"
      xsi:type="RabbitMq"
      useJSON="true"
      layout="${message}" />
</targets>
```

## Minimum Recommended Configuration

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

  <extensions>
    <add assembly="Nlog.RabbitMQ.Target" />
  </extensions>

  <targets async="true">
    <target name="RabbitMqTarget"
        xsi:type="RabbitMq"
        useJSON="true"
        layout="${message}" />
  </targets>

  <rules>
    <logger name="*" minlevel="Trace" writeTo="RabbitMqTarget"/>
  </rules>

</nlog>
```

Remember to mark your `NLog.config` file to be copied to the output directory!

## Full Configuration

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

	<extensions>
		<add assembly="Nlog.RabbitMQ.Target" />
	</extensions>

	<targets>
		<!-- these are the defaults (except 'topic' and 'appid'): -->
		<target name="RabbitMq"
				xsi:type="RabbitMq"
				appid="NLog.RabbitMq.DemoApp"
				correlationId=""
				messageType=""
				topic="DemoApp.Logging.${level}"
				username="guest"
				password="guest"
				hostname="localhost"
				exchange="app-logging"
				exchangeType="topic"
				clientProvidedName=""
				port="5672"
				vhost="/"
				maxBuffer="10240"
				heartBeatSeconds="3"
				Timeout="3000"
				layout="${message}"
				messageSource="nlog://${hostname}/${logger}"
				contentType="text/plain"
				UseJSON="false"
				UseSsl="false"
				SslCertPath=""
				SslCertPassphrase=""
				Compression="None"
				DeliveryMode="NonPersistent">
			<field key="threadid" layout="${threadid}" />
			<field key="hostname" layout="${hostname}" />
		</target>
	</targets>

	<rules>
		<logger name="*" minlevel="Trace" writeTo="RabbitMQTarget"/>
	</rules>

</nlog>
```

**Recommendation - async wrapper target**

Make the targets tag look like this: `<targets async="true"> ... </targets>` so that
a failure of communication with RabbitMQ doesn't slow the application down. With this configuration
an overloaded message broker will have 10000 messages buffered in the logging application
before messages start being discarded. A downed message broker will have its messages
in the _inner_ target (i.e. RabbitMQ-target), not in the async buffer (as the RabbitMQ-target
will not block which is what AsyncWrapperTarget buffers upon).

---

**Note:** As of vNEXT, the RabbitMQ target is itself asynchronous. Wrapping in `<targets async="true">` is optional and may provide additional buffering, but is no longer required for non-blocking logging.

## Important - shutting it down!

Because NLog doesn't expose a single method for shutting everything down (but loads automatically by static properties - the loggers' first invocation to the framework) - you need to add this code to the exit of your application!

```csharp
LogManager.Shutdown();
```

## Value-Add - How to use with LogStash?

Make sure you are using the flag `useJSON='true'` in your configuration, then you [download logstash](http://logstash.net/)! Place it in a folder, and add a file that you call 'logstash.conf' next to it:

```
input {
  amqp {
    durable => true
    exchange => "app-logging"
    exclusive => false
    format => "json_event"
    host => "localhost"
    key => "#"
    name => ""
    passive => false
    password => "guest"
    port => 5672
    prefetch_count => 10
    ssl => false
    # tags => ... # array (optional)
    type => "nlog"
    user => "guest"
    verify_ssl => false
    vhost => "/"
  }
}

output {
  # Emit events to stdout for easy debugging of what is going through
  # logstash.
  stdout { }

  # This will use elasticsearch to store your logs.
  # The 'embedded' option will cause logstash to run the elasticsearch
  # server in the same process, so you don't have to worry about
  # how to download, configure, or run elasticsearch!
  elasticsearch { embedded => true }
}
```

You then start the monolithic logstash: `java -jar logstash-1.1.0-monolithic.jar agent -f logstash.conf -- web`.
Now you can surf to http://127.0.0.1:9292 and search your logs that you're generating
