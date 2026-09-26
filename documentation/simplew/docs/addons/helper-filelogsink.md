# FileLogSink

<AddonMeta
    package="SimpleW.Helper.FileLogSink"
    status="official"
    license="MIT"
    license-url="https://github.com/Stratdev3/SimpleW/blob/master/licence"
    experimental
/>


The `SimpleW.Helper.FileLogSink` package provides buffered file logging for SimpleW, with a bounded queue and one background writer.


## Features

- UTF-8 text logs with timestamps, levels, sources, trace/span IDs and exceptions.
- UTC hourly or daily rotation, optionally combined with an approximate file size limit.
- Optional retention by file count.
- Batched writes, periodic flushing and explicit asynchronous flushing.


## Requirements

- .NET 8, .NET 9 or .NET 10.
- SimpleW 26.1.x.


## Installation

Install the `SimpleW.Helper.FileLogSink` addon and import `SimpleW.Helper.FileLogSink` to use `FileLogSink`, `FileLogSinkOptions` and `FileLogRollingInterval`. Logging APIs such as `Log` and `LogEntry` remain in `SimpleW.Observability`.

Add this package reference to your project:

```xml
<PackageReference Include="SimpleW.Helper.FileLogSink" Version="26.1.0" />
```

The sink accepts entries through `fileSink.Write`, so it also works with `Log.SetSink(fileSink.Write)` to replace existing sinks.

See the [changelog](./helper-filelogsink-changelog.md).


## Minimal example

Setup has two steps:

1. Construct `FileLogSink`. The constructor creates the directory, opens the file and applies retention. Startup errors are thrown here.
2. Register `fileSink.Write` with `Log.AddSink`.

The file is ready when construction returns. No initial `FlushAsync()` call is needed.

```csharp
using SimpleW.Helper.FileLogSink;
using SimpleW.Observability;

// 1. Open the file and start the background writer.
await using FileLogSink fileSink = new(new FileLogSinkOptions {
    FilePath = "logs/server.log"
});

// 2. Send SimpleW logs to this sink.
Log.AddSink(fileSink.Write);

// 3. use logger
Log.Info("MyApp", "Application started");
```

`await using` awaits disposal when the scope ends; construction itself is synchronous.


## Rotating Text Files

The default configuration combines **daily UTC rotation and an approximate 100 MiB size limit**, flushes every second and keeps all old files. Configure either rotation independently, or combine them:

```csharp
using SimpleW.Helper.FileLogSink;
using SimpleW.Observability;

await using FileLogSink fileSink = new(new FileLogSinkOptions {
    FilePath = "logs/server.log",
    RollingInterval = FileLogRollingInterval.Hour,
    MaxFileSizeBytes = 50L * 1024 * 1024,
    RetainedFileCountLimit = 48,
    QueueCapacity = 8192,
    BufferSizeBytes = 64 * 1024,
    FlushInterval = TimeSpan.FromSeconds(1)
});
```

Set `RollingInterval = FileLogRollingInterval.None` for size-only rotation, or `MaxFileSizeBytes = null` for date-only rotation. Disabling both creates one segment per sink lifetime. `RetainedFileCountLimit = null` disables deletion.

For a base path of `logs/server.log`, files are named:

- Daily: `server-20260910-000001.log`.
- Hourly: `server-2026091014-000001.log`.
- Without calendar rotation: `server-000001.log`.

Every restart creates a new segment, without overwriting or appending to existing files. Calendar rotation occurs on the first entry processed in a new UTC period, using the writer's clock rather than the entry's timestamp. Size rotation estimates one byte per UTF-16 character in the complete formatted text, including timestamps, exceptions, line endings and buffered entries. This matches ASCII output but underestimates non-ASCII UTF-8 text, so actual files can exceed the configured size. An entry is never split; an entry whose estimate exceeds the limit occupies its own file.

Retention runs at startup and on rotation. It removes the oldest matching segments, ordered by UTC period and sequence. The limit includes the active file, which is never deleted. Only exact names for the configured base path and rotation scheme are considered; files from a different rotation scheme are left alone. Use a dedicated base path for each sink and process.

### Text format

Files use UTF-8 without a BOM and CRLF line endings, including multiline messages and exceptions:

```text
2026-09-10T14:23:45.1230000+00:00 [INF] MyApp Application started
2026-09-10T14:23:46.1230000+00:00 [ERR] MyApp [trace=0123456789abcdef0123456789abcdef] [span=0123456789abcdef] Request failed
System.InvalidOperationException: Example failure
```

The fixed format includes the UTC timestamp, three-letter level, source, optional trace/span IDs, message and complete exception text (including its stack trace when available).

### Performance

Calls enqueue entries into a bounded channel. One background worker formats them and writes through a buffered StreamWriter kept open for the active file. StreamWriter handles UTF-8 encoding and groups file writes. If the queue fills, producers wait for space; entries are not intentionally dropped. Capacity is measured in events, not bytes, and includes pending explicit flush requests.

### Optional explicit flushing

Normal logging uses the periodic flush configured by `FlushInterval`, including when traffic stops.
Call `FlushAsync` only when application code needs to wait for already accepted logs to be written and flushed:

```csharp
Log.Info("MyApp", "Checkpoint reached");
await fileSink.FlushAsync();
```

This call is optional and is not an initialization step. Cancellation stops waiting without discarding accepted entries. Flush does not force a durable hardware write; abrupt termination or power loss can lose buffered data.

### Shutdown and errors

Stop and await log producers, remove the sink, then dispose it. Both `Dispose()` and `DisposeAsync()` drain the queue and close the file; repeated disposal waits for the same shutdown. Synchronous disposal blocks until the writer finishes. Calls after closure throw `ObjectDisposedException`.

Startup failures are thrown by the constructor. After construction, an error during background formatting, writing, rotation or retention permanently faults the sink. `LastError` exposes the first exception; pending flushes, blocked producers, subsequent writes/flushes and disposal report the failure. Queued entries cannot be guaranteed after such a failure. The sink does not retry or report failures through `Log`.

The sink does not change `Log.MinimumLevel`. Set it to `LogLevel.Trace` to forward all levels; the default global level is `Information`. Other registered sinks remain synchronous and contribute their own cost to each log call.

See the [file sink API and option defaults](#api-reference) for the complete configuration.


## API reference

Provided by the separate `SimpleW.Helper.FileLogSink` package, in the `SimpleW.Helper.FileLogSink` namespace. The `Log` and `LogEntry` types remain in `SimpleW.Observability`. Register `Write` with `Log.AddSink` or `Log.SetSink`, and remove it before disposal. See [Minimal example](#minimal-example) and [Rotating Text Files](#rotating-text-files) for usage and rotation rules.

```csharp
public sealed class FileLogSink : IDisposable, IAsyncDisposable {
    public FileLogSink(FileLogSinkOptions options);
    public Exception? LastError { get; }
    public void Write(LogEntry entry);
    public Task FlushAsync(CancellationToken cancellationToken = default);
    public void Dispose();
    public ValueTask DisposeAsync();
}

public enum FileLogRollingInterval {
    None,
    Hour,
    Day
}
```

### Configuration options

`FileLogSinkOptions` settings are copied and validated at construction; changing the original object afterwards has no effect.

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `FilePath` | `string` | Required | Base filename; parent directories are created. Relative paths are resolved at construction. |
| `RollingInterval` | `FileLogRollingInterval` | `Day` | UTC calendar rotation, or `None`. |
| `MaxFileSizeBytes` | `long?` | `104857600` (100 MiB) | Approximate size limit using one byte per formatted UTF-16 character; `null` disables it. Non-ASCII text can exceed the estimate. Entries are never split. |
| `RetainedFileCountLimit` | `int?` | `null` | Maximum matching files, including the active file; `null` disables deletion. |
| `QueueCapacity` | `int` | `8192` | Bounded queue capacity; writers wait for space when full. |
| `BufferSizeBytes` | `int` | `65536` | Byte budget for the StreamWriter character buffer: divided by two, with a minimum of 128 characters. StreamWriter also manages its encoding buffer. |
| `FlushInterval` | `TimeSpan` | 1 second | Periodic flush, including when idle. |

Non-null numeric limits and capacities must be strictly positive. `FlushInterval` must be between 1 and 4294967294 milliseconds, inclusive (the timer's supported range). Invalid options throw at construction.

`Write` normally just enqueues; only saturation makes it wait. `FlushAsync` inserts a queue barrier and completes after previously accepted events have been written and flushed. Its cancellation token cancels waiting, not accepted work. Neither operation forces an fsync.

The destination is initialized synchronously during construction. Once the constructor returns, entries can be enqueued immediately; subsequent writes, rotation and flushing run in the background. `LastError` is `null` until the first background failure. Such a failure permanently closes the queue, releases waiting callers and is propagated by pending flushes, subsequent writes/flushes and disposal. After a normal closure, writes and flushes throw `ObjectDisposedException`.

Dispose is idempotent and drains accepted events before closing the file. Stop producers and unregister `Write` before disposing. Use one sink/process per base path; multi-process sharing and crash durability are not supported.
