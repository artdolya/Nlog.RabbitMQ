# Nlog.RabbitMQ.Target
RabbitMQ Target for popular NLog logging tool

[![NuGet](https://img.shields.io/nuget/v/Nlog.RabbitMQ.Target.svg)](https://www.nuget.org/packages/Nlog.RabbitMQ.Target/)

## Minimum Recommended Configuration

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

  <extensions>
    <add assembly="Nlog.RabbitMQ.Target" />
  </extensions>

  <targets async="true">
    <target name="RabbitMQTarget"
        xsi:type="RabbitMQ"
        useJSON="true"
        layout="${message}" />
  </targets>

  <rules>
    <logger name="*" minlevel="Trace" writeTo="RabbitMQTarget"/>
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
		<target name="RabbitMQTarget"
				xsi:type="RabbitMQ"
				appid="NLog.RabbitMQ.DemoApp"
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
				layout="${longdate}|${level:uppercase=true}|${logger}|${message}"
				messageSource="nlog://${machinename}/${logger}"
				UseJSON="false"
				UseLayoutAsMessage="false"
				UseSsl="false"
				SslCertPath=""
				SslCertPassphrase=""
				Compression="None"
				DeliveryMode="NonPersistent">
			<field key="threadid" layout="${threadid}" />
			<field key="machinename" layout="${machinename}" />
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
in the *inner* target (i.e. RabbitMQ-target), not in the async buffer (as the RabbitMQ-target
will not block which is what AsyncWrapperTarget buffers upon).

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
