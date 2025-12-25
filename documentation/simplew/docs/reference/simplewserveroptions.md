# SimpleWServerOptions

The `SimpleWServerOptions` allows you to pass configuration options to the main `SimpleWServer` class.


```csharp
/// <summary>
/// This option will set the maximum length of the pending connections queue.
/// </summary>
public int ListenBacklog { get; set; } = 1024;
```

```csharp
/// <summary>
/// Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
/// Will work only if socket is bound on IPv6 address.
/// </summary>
public bool DualMode { get; set; }
```

```csharp
/// <summary>
/// This option will enable/disable Nagle's algorithm for TCP protocol
/// </summary>
public bool TcpNoDelay { get; set; }
```

```csharp
/// <summary>
/// This option will enable/disable SO_REUSEADDR if the OS support this feature
/// </summary>
public bool ReuseAddress { get; set; }
```

```csharp
/// <summary>
/// This option will enable/disable SO_EXCLUSIVEADDRUSE if the OS support this feature
/// </summary>
public bool ExclusiveAddressUse { get; set; }
```

```csharp
/// <summary>
/// This option will enable SO_REUSEPORT if the OS support this feature (linux only)
/// </summary>
public bool ReusePort { get; set; }
```

```csharp
/// <summary>
/// This option will run the accept socket on each machine's core
/// </summary>
public bool AcceptPerCore { get; set; }
```

```csharp
/// <summary>
/// This option will setup SO_KEEPALIVE if the OS support this feature
/// </summary>
public bool TcpKeepAlive { get; set; }
```

```csharp
/// <summary>
/// The number of seconds a TCP connection will remain alive/idle before keepalive probes are sent to the remote
/// </summary>
public int TcpKeepAliveTime { get; set; } = -1;
```

```csharp
/// <summary>
/// The number of seconds a TCP connection will wait for a keepalive response before sending another keepalive probe
/// </summary>
public int TcpKeepAliveInterval { get; set; } = -1;
```

```csharp
/// <summary>
/// The number of TCP keep alive probes that will be sent before the connection is terminated
/// </summary>
public int TcpKeepAliveRetryCount { get; set; } = -1;
```

```csharp
/// <summary>
/// Option: receive buffer size
/// </summary>
public int ReceiveBufferSize { get; set; } = 16 * 1024;
```

```csharp
/// <summary>
/// Is the server started?
/// </summary>
public bool IsStarted { get; private set; }
```

```csharp
/// <summary>
/// Is the server currently stopping?
/// </summary>
public bool IsStopping { get; private set; } = false;
```

```csharp
/// <summary>
/// Idle timeout (if no data received during timeout, then close connection)
/// Set TimeSpan.MinValue to disable.
/// </summary>
public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);
```
