# Serilog


The [`SimpleW.Helper.Serilog`](https://www.nuget.org/packages/SimpleW.Helper.Serilog) package provides a **bridge between the SimpleW internal logging system and Serilog**.

It allows all logs emitted through `SimpleW.Observability.Log` to be automatically forwarded to a **Serilog** `ILogger` **instance**.

This integration keeps the **SimpleW logging API lightweight and allocation-friendly**, while letting developers use the full Serilog ecosystem (sinks, enrichers, structured logging, etc.).


## Features

It allows you to :
- Forward all **SimpleW logs** to Serilog
- Automatically **detect the minimum logging level** from the provided Serilog logger
- Preserve **TraceId / SpanId** from `System.Diagnostics.Activity`
- Attach the SimpleW **log source** as a Serilog property
- Add **custom enrichers**


## Requirements

- .NET 8.0
- SimpleW (core server)
- Serilog (>= 4.2)


## Installation

```sh
$ dotnet add package SimpleW.Helper.Serilog --version 26.0.0-beta.20260202-1339
```


## Configuration options

| Option | Default | Description |
|---|---|---|
| MinimumLevel | `auto` | Overrides the SimpleW global minimum log level. When `null`, the level is automatically detected from the provided Serilog `ILogger` using `ILogger.IsEnabled()`. |
| Mode | `SinkMode.Add` | Determines how the Serilog bridge is attached. `Add` chains the sink with existing SimpleW sinks. `Replace` replaces the current SimpleW sink entirely. |
| IncludeSource | `true` | When enabled, the SimpleW log source (`entry.Source`) is attached to the Serilog log event as a property. |
| IncludeTraceAndSpan | `true` | When an active `System.Diagnostics.Activity` exists, the bridge adds `trace_id` and `span_id` properties to the Serilog log event. |
| SourcePropertyName | `"simplew_source"` | Name of the Serilog property used to store the SimpleW log source. |
| TraceIdPropertyName | `"trace_id"` | Name of the Serilog property used to store the `Activity.TraceId`. |
| SpanIdPropertyName | `"span_id"` | Name of the Serilog property used to store the `Activity.SpanId`. |
| Enricher | `null` | Optional function allowing custom enrichment of the Serilog logger per log entry. Receives `(ILogger logger, LogEntry entry)` and must return the enriched `ILogger`. |


## Minimal Example

Create your Serilog logger normally :

```csharp
using Serilog;
using SimpleW.Helper.Serilog;

ILogger logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();
```

Install the bridge :

```csharp
var bridge = SimpleWSerilogBridge.Install(logger, configure: options => {
    options.IncludeSource = true;
});
```

Once installed, every log emitted via :

```csharp
Log.Info("MySource", "Hello world");
```

will be forwarded to Serilog.


## Automatic Minimum Level Detection

When installing the bridge, SimpleW automatically detects the minimum level enabled in the Serilog logger.

Example :

```csharp
new LoggerConfiguration().MinimumLevel.Warning()
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
 └─ Serilog bridge
```

If you want Serilog to be the only logging target, replace the sink:

```csharp
SimpleWSerilogBridge.Install(logger, replaceExistingSink: true);
```


## Activity / Distributed Tracing

When a `System.Diagnostics.Activity` is active (for example when using OpenTelemetry), the bridge automatically attaches :

```
trace_id
span_id
```

These properties allow Serilog logs to correlate with distributed traces.

SimpleW telemetry is based on `System.Diagnostics.Activity` and integrates naturally with the OpenTelemetry ecosystem. 
