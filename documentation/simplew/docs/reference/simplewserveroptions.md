# SimpleWServerOptions

`SimpleWSServerOptions` contains the server-level HTTP/session options used by `SimpleWServer`.

Default socket/listener tuning belongs to [`SimpleWEngineOptions`](./simplewengineoptions.md) and is configured with `server.UseEngine(options => ...)`.


## Session options

```csharp
/// <summary>
/// Idle timeout (if no data received during timeout, then close connection)
/// Set TimeSpan.MinValue to disable.
/// </summary>
public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);
```


## Security options

```csharp
/// <summary>
/// Max size of request headers in bytes (default: 64 KB)
/// </summary>
public int MaxRequestHeaderSize { get; set; } = 64 * 1024;
```

```csharp
/// <summary>
/// Max size of request body in bytes (default: 10 MB)
/// </summary>
public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;
```

```csharp
/// <summary>
/// Maximum time allowed to receive a complete HTTP request header block
/// Protects against slowloris attacks that send headers very slowly.
/// Use <see cref="TimeSpan.MinValue"/> to disable this protection.
/// Default recommended value: 5 seconds.
/// </summary>
public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(5);
```

```csharp
/// <summary>
/// Minimum sustained body receive rate, in bytes per second,
/// required after <see cref="RequestBodyGracePeriod"/> has elapsed.
/// If the request body progresses slower than this rate, the connection
/// is considered too slow and will be closed.
/// Set 0 to disable this protection.
/// Default: 256 bytes/s.
/// </summary>
public int MinRequestBodyDataRateBytesPerSecond { get; set; } = 256;
```

```csharp
/// <summary>
/// Grace period applied before enforcing
/// <see cref="MinRequestBodyDataRateBytesPerSecond"/>.
/// This gives the client a short time window before the minimum body
/// receive rate check begins.
/// Set <see cref="TimeSpan.Zero"/> for no grace period.
/// Default: 5 seconds.
/// </summary>
public TimeSpan RequestBodyGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
```


## Handler

```csharp
/// <summary>
/// When a RequestAborted cancellation token is get inside an async handler,
/// a task is run periodically to poll the underlying socket
/// to detect when the client disconnects.
/// Default polling every 250ms (TimeSpan.Zero to disable)
/// </summary>
public TimeSpan SocketDisconnectPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
```