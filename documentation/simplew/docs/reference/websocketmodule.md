# WebsocketModule

The `WebSocketModule` installs a WebSocket endpoint on the server.
It maps a single WebSocket route, upgrades the HTTP connection to WebSocket, then routes incoming messages by `op`.


## Definition

```csharp
/// <summary>
/// Use WebSocket Module
/// It setups a Middleware
/// </summary>
public static SimpleWServer UseWebSocketModule(this SimpleWServer server, Action<WebSocketOptions>? configure = null)
```

The options are the following

```csharp
/// <summary>
/// Single WS endpoint path (default "/ws")
/// </summary>
public string Prefix { get; set; } = "/ws";
```

```csharp
/// <summary>
/// Max message size after reassembly (default 1 MiB)
/// </summary>
public int MaxMessageBytes { get; set; } = 1 * 1024 * 1024;
```

```csharp
/// <summary>
/// If set, must be present in Sec-WebSocket-Protocol
/// </summary>
public string? RequiredSubProtocol { get; set; }
```

```csharp
/// <summary>
/// If set, periodically send ping to keep proxies/load balancers happy
/// </summary>
public TimeSpan? KeepAliveInterval  { get; set; }
```

```csharp
/// <summary>
/// Global hub to allow broadcasts to rooms (and/or an "all" room)
/// </summary>
public WebSocketHub Hub { get; } = new();
```

```csharp
/// <summary>
/// Optional: automatically join every connection into this room
/// Handy to broadcast to everyone: options.Hub.BroadcastTextAsync("__all", ...)
/// Set to null to disable auto join
/// </summary>
public string? AutoJoinRoom { get; set; } = "__all";
```

```csharp
/// <summary>
/// Message router (exact match on msg.Op)
/// </summary>
public WebSocketRouter Router { get; } = new();
```

```csharp
/// <summary>
/// Called after successful handshake, before the router
/// (Typical: auth handshake, join rooms...)
/// </summary>
public Func<WebSocketConnection, WebSocketContext, ValueTask>? OnConnect { get; set; }
```

```csharp
/// <summary>
/// Called when the connection is closed (finally)
/// </summary>
public Func<WebSocketConnection, WebSocketContext, ValueTask>? OnDisconnect { get; set; }
```

```csharp
/// <summary>
/// Register a handler for an op
/// </summary>
/// <param name="op"></param>
/// <param name="handler"></param>
public void Map(string op, WebSocketMessageHandler handler);
```

```csharp
/// <summary>
/// Register fallback handler
/// </summary>
/// <param name="handler"></param>
public void OnUnknown(WebSocketMessageHandler handler);
```

```csharp
/// <summary>
/// Binary handler
/// </summary>
public void OnBinary(Func<WebSocketConnection, WebSocketContext, ReadOnlyMemory<byte>, ValueTask> handler)
```


## Envelope

Text handlers receive a `WebSocketEnvelope`.

```csharp
    /// <summary>
    /// Envelope used for routing
    /// </summary>
    /// <param name="Op"></param>
    /// <param name="Id"></param>
    /// <param name="IsJson"></param>
    /// <param name="RawUtf8"></param>
    /// <param name="PayloadUtf8"></param>
public readonly record struct WebSocketEnvelope(
    string Op,
    string? Id,
    bool IsJson,
    ReadOnlyMemory<byte> RawUtf8,
    ReadOnlyMemory<byte> PayloadUtf8
)
```

```csharp
/// <summary>
/// Try deserialize full payload into T.
/// Returns false if there is no payload or if deserialization fails.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="value"></param>
/// <param name="options"></param>
/// <returns></returns>
public bool TryDeserializePayload<T>(out T? value, JsonSerializerOptions? options = null)
```

## Example

See [examples](../guide/websockets.md) of use.