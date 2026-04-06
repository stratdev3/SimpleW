# Logging

SimpleW provides a **minimal and extremely lightweight logging system**.

The [`logging`](../reference/logger.md) system is intentionally simple and is designed to use directly from server code and modules :

- No external dependencies
- No complex configuration
- No formatting pipeline
- Very low overhead


## Create a Logger

A logger is typically declared as a **static readonly property** in a class.

```csharp
// custom string
private static readonly ILogger _log = new Logger("MyClass");
// or Class
private static readonly ILogger _log = new Logger<MyClass>();
```


## Log Levels

SimpleW defines several log levels :
- **Trace** : _You're desperately tracking something you can't debug, good luck_
- **Debug** : _It smells shit, I want maximum informations_
- **Info** : _Normal Operation, foods my social media infinite scroll_
- **Warning** : _It's like a compiler warning, nobody take care but it should_
- **Error** : _Prod can continue but fix that error quickly, seriously_
- **Fatal** : _Rest In Peace_


## Minimum Log Level

SimpleW allows configuring a **minimum log level**. Any log entry below this level will be ignored and **will not be sent to sinks**.

The minimum level can be configured globally using :

```csharp
Log.MinimumLevel = LogLevel.Debug;
```

The default level is `Information`.


## Lazy Logging

Expensive log messages should use the **lazy overload**.

Instead of :

```csharp
_log.Debug($"Parsed {headers.Count} headers");
```

Prefer :

```csharp
_log.Debug(() => $"Parsed {headers.Count} headers");
```

This avoids constructing the string when the log level is disabled.


## Sinks

SimpleW logs are emitted through **sinks**.

A sink is simply a function that receives a `LogEntry`.

```csharp
Action<LogEntry>
```

This allows logs to be forwarded to :
- the console
- files
- external logging frameworks
- custom monitoring systems


## Configuring Sinks

The `Log` class exposes several methods to configure sinks.

### Replace all sinks

```csharp
Log.SetSink(entry => {
    Console.WriteLine($"{entry.TimestampUtc:O} [{entry.Level}] {entry.Source} {entry.Message}");
});
```

### Add an additional sink

```csharp
Log.AddSink(entry => {
    File.AppendAllText("server.log", $"{entry.TimestampUtc:O} [{entry.Level}] {entry.Source} {entry.Message}\n");
});
```

This allows **multiple sinks** to receive the same log entry.

### Remove a sink

```csharp
Log.RemoveSink(mySink);
```


## Integrating External Logging Systems

SimpleW logging can easily be connected to **external logging frameworks or monitoring systems**.

This is done by registering a **sink** that forwards `LogEntry` objects to another system (see the example above).

See Addons to find all the available logging helpers to integrate well-known solutions like [Serilog](../addons/helper-serilog.md) or [Log4net](../addons/helper-log4net.md).
