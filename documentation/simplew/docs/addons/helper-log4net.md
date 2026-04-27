# Log4net

The [`SimpleW.Helper.Log4net`](https://www.nuget.org/packages/SimpleW.Helper.Log4net) package provides a **bridge between the SimpleW internal logging system and log4net**.

It allows all logs emitted through `SimpleW.Observability.Log` to be automatically forwarded to a **log4net** `ILog` instance.

This integration keeps the **SimpleW logging API lightweight and allocation-friendly**, while letting developers use the mature log4net ecosystem (appenders, layouts, filters, etc.).


## Features

It allows you to :
- Forward all **SimpleW logs** to log4net
- Automatically **detect the minimum logging level** from the provided log4net logger
- Preserve **TraceId / SpanId** from `System.Diagnostics.Activity`
- Attach the SimpleW **log source** as a log4net property
- Add **custom enrichers**


## Requirements

- .NET 8.0
- SimpleW (core server)
- Log4net (>= 3.2)


## Installation

```sh
$ dotnet add package SimpleW.Helper.Log4net --version 26.0.0
```


## Configuration options

| Option              | Default            | Description                                                                                                                                                      |
| ------------------- | ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| MinimumLevel        | `auto`             | Overrides the SimpleW global minimum log level. When `null`, the level is automatically detected from the provided log4net logger.                               |
| Mode                | `SinkMode.Add`     | Determines how the log4net bridge is attached. `Add` chains the sink with existing SimpleW sinks. `Replace` replaces the current SimpleW sink entirely.          |
| IncludeSource       | `true`             | When enabled, the SimpleW log source (`entry.Source`) is attached to the log4net context properties.                                                             |
| IncludeTraceAndSpan | `true`             | When an active `System.Diagnostics.Activity` exists, the bridge adds `trace_id` and `span_id` properties to the log event.                                       |
| SourcePropertyName  | `"simplew_source"` | Name of the log4net property used to store the SimpleW log source.                                                                                               |
| TraceIdPropertyName | `"trace_id"`       | Name of the property used to store the `Activity.TraceId`.                                                                                                       |
| SpanIdPropertyName  | `"span_id"`        | Name of the property used to store the `Activity.SpanId`.                                                                                                        |
| Enricher            | `null`             | Optional callback allowing custom enrichment of the log4net properties for each log entry. Receives `(IDictionary<string, object?> properties, LogEntry entry)`. |


## Minimal example

Configure a simple log4net console logger in code :

```csharp
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using SimpleW.Helper.Log4net;

var hierarchy = (Hierarchy)LogManager.GetRepository();

var layout = new PatternLayout("%date %-5level %message%newline");
layout.ActivateOptions();

var consoleAppender = new ConsoleAppender {
    Layout = layout
};
consoleAppender.ActivateOptions();

hierarchy.Root.AddAppender(consoleAppender);
hierarchy.Root.Level = log4net.Core.Level.Info;
hierarchy.Configured = true;

ILog logger = LogManager.GetLogger(typeof(Program));
```

Install the bridge :

```csharp
var bridge = SimpleWLog4NetBridge.Install(logger, configure: options => {
    options.IncludeSource = true;
});
```

Once installed, every log emitted via :

```csharp
Log.Info("MySource", "Hello world");
```

will be forwarded to log4net.


## Automatic Minimum Level Detection

When installing the bridge, SimpleW automatically detects the minimum level enabled in the log4net logger.

Example :

```csharp
hierarchy.Root.Level = log4net.Core.Level.Warn;
```

The bridge will automatically configure:

```csharp
SimpleW.Log.MinimumLevel = Warning
```

This ensures that disabled log levels are filtered **before log creation**, preserving the performance characteristics of SimpleW logging.


## Replace vs Add Sink

By default the bridge adds a sink.

```csharp
options.Mode = SinkMode.Add;
```

This allows multiple logging targets.

Example :

```
SimpleW
 ├─ Console sink
 └─ log4net  bridge
```

If you want Log4net to be the only logging target, replace the sink:

```csharp
SimpleWLog4netBridge.Install(logger, replaceExistingSink: true);
```


## Activity / Distributed Tracing

When a `System.Diagnostics.Activity` is active (for example when using OpenTelemetry), the bridge automatically attaches :

```
trace_id
span_id
```

These properties are stored in **log4net LogicalThreadContext properties**, allowing layouts such as :

```
%date %-5level %property{trace_id} %property{span_id} %message%newline
```

This allows log4net logs to correlate with distributed traces.

SimpleW telemetry is based on `System.Diagnostics.Activity` and integrates naturally with the OpenTelemetry ecosystem. 
