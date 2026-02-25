# Logger

This class is used for logging.


## Constructor

Prefix the logger with a string

```csharp
private static readonly ILogger _log = new Logger("MyClass");
```

Prefix the logging with a Type as string

```csharp
private static readonly ILogger _log = new Logger<MyClass>();
```

## Sinks

```csharp
/// <summary>
/// Set Sink
/// </summary>
/// <param name="sink"></param>
public static void SetSink(Action<LogEntry> sink);

/// <summary>
/// Set Sink
/// </summary>
/// <param name="sink"></param>
/// <param name="globalMinimumLevel"></param>
public static void SetSink(Action<LogEntry> sink, LogLevel globalMinimumLevel);
```

```csharp
/// <summary>
/// Add Sink
/// </summary>
/// <param name="sink"></param>
public static void AddSink(Action<LogEntry> sink)
```

```csharp
/// <summary>
/// Remove Sink
/// </summary>
/// <param name="sink"></param>
public static void RemoveSink(Action<LogEntry> sink)
```


## Logger

```csharp
/// <summary>
/// You're desperately tracking something you can't debug, good luck
/// </summary>
/// <param name="msgFactory"></param>
public void Trace(Func<string> msgFactory);

/// <summary>
/// You're desperately tracking something you can't debug, good luck
/// </summary>
/// <param name="message"></param>
public void Trace(string message);
```

```csharp
/// <summary>
/// It smells shit, I want maximum informations
/// </summary>
/// <param name="message"></param>
public void Debug(string message)

/// <summary>
/// It smells shit, I want maximum informations
/// </summary>
/// <param name="msgFactory"></param>
public void Debug(Func<string> msgFactory)
```

```csharp
/// <summary>
/// Normal Operation, foods my social media infinite scroll
/// </summary>
/// <param name="message"></param>
public void Info(string message)
```

```csharp
/// <summary>
/// It's like a compiler warning, nobody take care but it should
/// </summary>
/// <param name="message"></param>
public void Warn(string message)

/// <summary>
/// It's like a compiler warning, nobody take care but it should
/// </summary>
/// <param name="message"></param>
/// <param name="ex"></param>
public void Warn(string message, Exception ex)
```

```csharp
/// <summary>
/// Prod can continue but fix that error quickly, seriously
/// </summary>
/// <param name="message"></param>
public void Error(string message)

/// <summary>
/// Prod can continue but fix that error quickly, seriously
/// </summary>
/// <param name="message"></param>
/// <param name="ex"></param>
public void Error(string message, Exception ex)
```

```csharp
/// <summary>
/// Rest In Peace
/// </summary>
/// <param name="message"></param>
public void Fatal(string message);

/// <summary>
/// Rest In Peace
/// </summary>
/// <param name="message"></param>
/// <param name="ex"></param>
public void Fatal(string message, Exception ex)
```


## Log Levels

```csharp
/// <summary>
/// LogLevel
/// </summary>
public enum LogLevel : byte {
    /// <summary>
    /// I want to fill the 2To SSD quickly
    /// </summary>
    Trace,
    /// <summary>
    /// I can't debug on my laptop, so let's do it in prod
    /// </summary>
    Debug,
    /// <summary>
    /// Normal operation, it's like infinite scroll on social media
    /// </summary>
    Information,
    /// <summary>
    /// I'll look into it, next monday, after my coffee
    /// </summary>
    Warning,
    /// <summary>
    /// Not critical but not warning either
    /// </summary>
    Error,
    /// <summary>
    /// Make me wake up at 3:00 AM saturday
    /// </summary>
    Fatal,
}
```

```csharp
/// <summary>
/// Minimum LogLevel
/// </summary>
public static LogLevel MinimumLevel
```
