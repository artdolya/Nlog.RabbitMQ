# Nlog.RabbitMQ.Target

RabbitMQ Target for popular NLog logging tool

[![NuGet](https://img.shields.io/nuget/v/Nlog.RabbitMQ.Target.svg)](https://www.nuget.org/packages/Nlog.RabbitMQ.Target/)

Forked from https://github.com/adolya/Nlog.RabbitMQ

Changed most of target parameters to be formatted using `Layout.Render(...)`

Renamed to a different package ID in order to be able to publish a nuget.

## Changes for ASPNET.CORE

The following configuration is now supported:

```xml
  ...
  <!--  Notification Service -->
  <target name="NameOfThisTarget"
      xsi:type="RabbitMQ"
      username="${configsetting:name=<String>:default=<Default Value>}"
      password="${configsetting:name=<String>:default=<Default Value>}"
      hostname="${configsetting:name=<String>:default=<Default Value>}"
      port="${configsetting:name=<String>:default=<Default Value>}"
      port="${configsetting:name=<String>:default=<Default Value>}"
      exchange="${configsetting:name=<String>:default=<Default Value>}"
      heartBeatSeconds="30"
      UseJSON="true"
      layout="${message}"
      DeliveryMode="NonPersistent">
    <field key="machineName" name="MachineName" layout="${machinename}"/>
    <field key="url" name="url" layout="${aspnet-request-url}" />
    <field key="callStack" name="callStack" layout="${stacktrace:separator=&#13;&#10;}" />
  </target>
  ...
```

Given the following config file `appsettings.Env.json`, in the environment `Env`:

```json
"Application": {
  "RabbitMQ": {
    "Host": "rabbitmq-server",
    "Port": "5672",
    "Username": "rabbitmquser",
    "Password": "password",
    "Vhost": "/",
    "Exchange": "test.exchange",
  }
}
```

The following should be in your `NLog.config` file:

```xml
<!--  Notification Service -->
<target name="NameOfThisTarget"
    xsi:type="RabbitMQ"
    username="${configsetting:name=Application.RabbitMQ.Username}"
    password="${configsetting:name=Application.RabbitMQ.Password}"
    hostname="${configsetting:name=Application.RabbitMQ.Host}"
    port="${configsetting:name=Application.RabbitMQ.Port}"
    vhost="${configsetting:name=Application.RabbitMQ.Vhost}"
    exchange="${configsetting:name=Application.RabbitMQ.Exchange}"
    heartBeatSeconds="30"
    UseJSON="true"
    layout="${message}"
    DeliveryMode="NonPersistent">
  <field key="machineName" name="MachineName" layout="${machinename}"/>
  <field key="url" name="url" layout="${aspnet-request-url}" />
  <field key="callStack" name="callStack" layout="${stacktrace:separator=&#13;&#10;}" />
</target>
```

## Minimum Recommended Configuration

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">

  <extensions>
    <add assembly="NLog.RabbitMQ.Target" />
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
		<add assembly="NLog.RabbitMQ.Target" />
	</extensions>

	<targets>
		<!-- these are the defaults (except 'topic' and 'appid'): -->
		<target name="RabbitMQTarget"
				xsi:type="RabbitMQ"
				appid="NLog.RabbitMQ.DemoApp"
				topic="DemoApp.Logging.{0}"
				username="guest" 
				password="guest" 
				hostname="localhost" 
				exchange="app-logging"
				port="5672"
				vhost="/"
				maxBuffer="10240"
				heartBeatSeconds="3"
				Timeout="3000"
				layout="${longdate}|${level:uppercase=true}|${logger}|${message}"
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
