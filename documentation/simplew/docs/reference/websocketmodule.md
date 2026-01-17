# WebsocketModule

The `WebsocketModule` is used to setup WebSockets.
This module setup a middleware.


## Definition

```csharp
/// <summary>
/// Use WebSocket Module
/// It setups a Middleware
/// </summary>
public static SimpleWServer UseWebSocketModule(this SimpleWServer server, Action<WebSocketOptions>? configure = null)
```

The options are the followings

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
/// Optional keep-alive ping
/// </summary>
public TimeSpan? PingInterval { get; set; }
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


## Example

```csharp:line-numbers
server.UseWebSocketModule(ws => {
    ws.Prefix = "/ws";

    // disable auto join
    ws.AutoJoinRoom = null;

    // store user/room per connection
    var userByConn = new ConcurrentDictionary<Guid, string>();
    var roomByConn = new ConcurrentDictionary<Guid, string>();

    static string ChatEvent(string kind, string room, string name, string? text = null) {
        var payload = new { kind, room, name, text = text ?? "" };
        var obj = new { op = "chat/event", payload };
        return JsonSerializer.Serialize(obj);
    }

    ws.Map("chat/join", async (conn, ctx, msg) => {
        if (!msg.TryGetPayload(out RoomName? m) || m == null) {
            return;
        }

        userByConn[conn.Id] = m.name;
        roomByConn[conn.Id] = m.room;

        await ctx.JoinRoomAsync(m.room, conn);

        // broadcast to others: "X joined"
        await ctx.Hub.BroadcastTextAsync(m.room, ChatEvent("join", m.room, m.name, $"{m.name} joined"), except: conn);

        // ack
        await conn.SendTextAsync(ChatEvent("join", m.room, m.name, $"joined {m.room}"));
    });

    ws.Map("chat/leave", async (conn, ctx, msg) => {
        if (!msg.TryGetPayload(out RoomName? m) || m == null) {
            return;
        }

        await ctx.LeaveRoomAsync(m.room, conn);
        await ctx.Hub.BroadcastTextAsync(m.room, ChatEvent("leave", m.room, m.name, $"{m.name} left"), except: conn);

        roomByConn.TryRemove(conn.Id, out _);
    });

    ws.Map("chat/msg", async (conn, ctx, msg) => {
        if (!msg.TryGetPayload(out RoomName? m) || m == null) {
            return;
        }

        // broadcast to all clients of the room
        await ctx.Hub.BroadcastTextAsync(m.room, ChatEvent("msg", m.room, m.name, m.text));
    });

    ws.OnUnknown(async (conn, ctx, msg) => {
        // for debug
        await conn.SendTextAsync(msg.IsJson ? $"unknown op: {msg.Op}" : "bad message: expected JSON {op,payload}");
    });

    ws.OnDisconnect = async (conn, ctx) => {
        // cleanup
        if (roomByConn.TryRemove(conn.Id, out var room) && userByConn.TryRemove(conn.Id, out var name)) {
            await ctx.Hub.BroadcastTextAsync(room, ChatEvent("leave", room, name, $"{name} disconnected"), except: conn);
        }
    };
});

class RoomName {
    public string room { get; set; }
    public string name { get; set; }
    public string text { get; set; }
}

```

See more [examples](../guide/websockets.md).