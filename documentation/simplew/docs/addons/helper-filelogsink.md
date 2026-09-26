# FileLogSink

<AddonMeta
    package="SimpleW.Helper.FileLogSink"
    status="official"
    license="MIT"
    license-url="https://github.com/Stratdev3/SimpleW/blob/master/licence"
    experimental
/>


The `SimpleW.Helper.FileLogSink` package provides file logging for .


## Features

- Text logs with timestamps, levels, sources, trace/span IDs and exceptions.
- Time rotation, optionally combined file size limit.
- Optional retention by file count.
- Batched writes, periodic flushing and explicit asynchronous flushing.


## Requirements

- .NET 8, .NET 9 or .NET 10.
- SimpleW 26.1.x.


## Installation

```sh
dotnet add package SimpleW.Service.FileLogSink
```

See the [changelog](./helper-filelogsink-changelog.md).


## Configuration Options

`FileLogSinkOptions` settings are copied and validated at construction; changing the original object afterwards has no effect.

| Option | Default | Description |
| --- | --- | --- |
| `FilePath` | Required | Base filename; parent directories are created. Relative paths are resolved at construction. |
| `RollingInterval` | `Day` | UTC calendar rotation (`None`, `Hour` or `Day`). |
| `MaxFileSizeBytes` | `104857600` (100 MiB) | Approximate size limit using one byte per formatted UTF-16 character; `null` disables it. Non-ASCII text can exceed the estimate. Entries are never split. |
| `RetainedFileCountLimit` | `null` | Maximum matching files, including the active file; `null` disables deletion. |
| `QueueCapacity` | `8192` | Bounded queue capacity; writers wait for space when full. |
| `BufferSizeBytes` | `65536` | Byte budget for the StreamWriter character buffer: divided by two, with a minimum of 128 characters. StreamWriter also manages its encoding buffer. |
| `FlushInterval` | 1 second | Periodic flush, including when idle. |


## Minimal example

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

The default configuration combines **daily UTC rotation and a 100 MiB size limit**, flushes every second and keeps all old files.

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

- Set `RollingInterval = FileLogRollingInterval.None` for size-only rotation.
- Set `MaxFileSizeBytes = null` for date-only rotation.
- Set `RetainedFileCountLimit = null` disables deletion.

For a base path of `logs/server.log`, files are named:

- Daily: `server-20260910-000001.log`.
- Hourly: `server-2026091014-000001.log`.
- Without calendar rotation: `server-000001.log`.

::: tip NOTE
Every restart creates a new file, without overwriting or appending to existing files.
Retention runs at startup and on rotation.
:::


### Optional explicit flushing

Normal logging uses the periodic flush configured by `FlushInterval`, including when traffic stops.
Call `FlushAsync` only when application code needs to wait for already accepted logs to be written and flushed:

```csharp
Log.Info("MyApp", "Checkpoint reached");
await fileSink.FlushAsync();
```

This call is optional and is not an initialization step. Cancellation stops waiting without discarding accepted entries. Flush does not force a durable hardware write; abrupt termination or power loss can lose buffered data.

