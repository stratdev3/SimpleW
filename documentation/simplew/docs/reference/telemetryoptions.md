# TelemetryOptions

The `TelemetryOptions` allows you to pass configuration options to [`SimpleWServer.ConfigureTelemetry()`](./simplewserver.md#telemetry) method.


## options

```csharp
/// <summary>
/// Record Exception
/// </summary>
public bool RecordException { get; set; } = true;
```

```csharp
/// <summary>
/// Include StackTrace
/// </summary>
public bool IncludeStackTrace { get; set; } = false;
```

```csharp
/// <summary>
/// TelemetryHandler
/// </summary>
public TelemetryHandler? EnrichWithHttpSession { get; set; } = null;
```
