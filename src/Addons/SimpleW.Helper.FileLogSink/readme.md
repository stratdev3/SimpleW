# SimpleW.Helper.FileLogSink

Buffered file logging for SimpleW, with a bounded queue and one background writer.


## Features

- UTF-8 text logs with timestamps, levels, sources, trace/span IDs and exceptions.
- UTC hourly or daily rotation, optionally combined with an approximate file size limit.
- Optional retention by file count.
- Batched writes, periodic flushing and explicit asynchronous flushing.
- Supports .NET 8, .NET 9 and .NET 10.


## Installation

Add a package reference to your project:

```xml
<PackageReference Include="SimpleW.Helper.FileLogSink" Version="26.1.0" />
```

The addon depends on SimpleW 26.1.x and has no additional third-party dependencies.


## Getting started

Construct the sink, then register `fileSink.Write` with `Log.AddSink`.
The constructor opens the file and reports startup errors before returning.
No initial `FlushAsync()` call is needed.

```csharp
using SimpleW.Helper.FileLogSink;
using SimpleW.Observability;

// 1. Open the file and start the background writer.
await using FileLogSink fileSink = new(new FileLogSinkOptions {
    FilePath = "logs/server.log",
    RollingInterval = FileLogRollingInterval.Day,
    MaxFileSizeBytes = 100L * 1024 * 1024,
    RetainedFileCountLimit = 30
});

// 2. Send SimpleW logs to this sink.
Log.AddSink(fileSink.Write);

// 3. start using log
Log.Info("MyApp", "Application started");
```

`await using` awaits disposal at the end of the scope; construction is synchronous.

By default, the sink rotates daily in UTC and at an estimated 100 MiB, flushes every second
and retains all old files. Set `RetainedFileCountLimit` to enable deletion of old files.
Size is estimated as one byte per UTF-16 character in the complete formatted log.
Non-ASCII text can exceed this estimate. Rotation never splits an entry.
A buffered StreamWriter handles UTF-8 encoding and groups file writes.

Use one sink/process for each base path. When the queue is full, producers wait
for space. Stop producers and unregister the sink before disposal.
`FlushAsync` flushes file buffers without forcing a durable hardware write;
abrupt termination can lose buffered events. Background failures are exposed
through `LastError` and propagated by writes, flushes and disposal.


## Migration from the core library

Reference this addon and replace the import used for the three file sink types
with `using SimpleW.Helper.FileLogSink;`. Keep `using SimpleW.Observability;`
for logging APIs such as `Log` and `LogEntry`.


## Documentation

See the [FileLogSink addon documentation](https://simplew.net/addons/helper-filelogsink)
for usage, rotation, retention and the complete API reference.


## Changelog

See [changelog.md](changelog.md).


## License

MIT.
