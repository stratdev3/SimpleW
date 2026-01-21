# Server Sent Events (SSE)

The `ServerSentEventsModule` provides **one-way real-time streaming** over HTTP using **Server-Sent Events** (SSE).

Server-Sent Events (SSE) is a server push technology enabling a client to receive automatic updates from a server via an HTTP connection.


## Key Concepts

### 1) A Single SSE Endpoint

The module exposes a **SSE endpoint**, configured via `ServerSentEventsOptions.Prefix` (default: `/ws`).

Even if you map `/sse/*`, the handler enforces **strict match**, so anything under `/sse/xxx` will return **404**.


### 2) SSE Handshake (HTTP streaming)

The module performs an SSE handshake and takes ownership of the underlying `HttpSession` connection :

- `Content-Type: text/event-stream; charset=utf-8`
- `Cache-Control: no-cache`
- `Connection: keep-alive`
- disables compression + content-length
- also adds `X-Accel-Buffering: no` (NGINX proxy buffering killer) 

It then calls `TryTakeTransportOwnership()` to stop the normal HTTP parsing loop and switch to stream ownership. 


### 3) Hub and Rooms

The module includes a built-in ServerSentEventsHub with a room system :
- connections can **join / leave rooms**
-broadcasts to a room send events to all connections in that room
-rooms are auto-cleaned when empty
- connections are removed on close automatically


## Installation

Install the module using :

```csharp
server.UseServerSentEventsModule();
```

Or with options :

```csharp
server.UseServerSentEventsModule(sse => {
    sse.Prefix = "/sse";
});
```

See all [options](../reference/serversenteventsmodule.md).


## Basic Usage

::: code-group

```csharp [server]
using System.Net;
using System.Text.Json;
using SimpleW;
using SimpleW.Modules;

var server = new SimpleWServer(IPAddress.Any, 2015);

server.UseServerSentEventsModule(sse => {
    sse.Prefix = "/sse";

    sse.OnConnect = async (conn, ctx) => {
        await conn.SendEventAsync("hello world", @event: "welcome");

        var payload = JsonSerializer.Serialize(new { now = DateTime.UtcNow });
        await conn.SendEventAsync(payload, @event: "time");
    };
});

await server.RunAsync();
```

```js [browser]
const es = new EventSource("/sse");

es.onmessage = (e) => console.log("message:", e.data);
es.addEventListener("time", (e) => console.log("time:", e.data));
es.addEventListener("welcome", (e) => console.log("welcome:", e.data));
```

:::


## Connection API (ServerSentEventsConnection)

Every client connection is represented by a `ServerSentEventsConnection`.

### Useful properties

- `Guid Id` – connection identifier 
- `object? Tag` – free bag to attach anything 
- `bool IsClosed` – indicates if connection is closed 
- `event Action<ServerSentEventsConnection>? Closed` – triggered once on close

### Send an event

```csharp
await conn.SendEventAsync("hello", @event: "msg");

// or with optional metadata
await conn.SendEventAsync(
    data: "payload",
    @event: "chat",
    id: "42",
    retryMs: 2000
);
```

### Send a comment (keep alive)

```csharp
await conn.SendCommentAsync("ping");
```

## Hub API (ServerSentEventsHub)

Rooms are stored in-memory in a ConcurrentDictionary.

### Join / Leave

```csharp
await hub.JoinAsync("my-room", conn);
await hub.LeaveAsync("my-room", conn);
```

### Broadcast an event (structured)

```csharp
await hub.BroadcastAsync("room", new ServerSentEventsMessage {
    Event = "tick",
    Data = "hello"
});
```

### Broadcast text (convenience)

```csharp
await hub.BroadcastTextAsync("room", "hello", @event: "message");
```

### Broadcast to many rooms

```csharp
await hub.BroadcastManyAsync(new[] { "r1", "r2" },
    new ServerSentEventsMessage { Event = "update", Data = "payload" }
);
```

> Note: broadcasting to many rooms does **not deduplicate** connections. If a connection is in multiple rooms, it may receive duplicates.

### Message format (`ServerSentEventsMessage`)

SSE message frame builder :

```csharp
public sealed class ServerSentEventsMessage {
    public string? Id { get; set; }
    public string? Event { get; set; }
    public int? RetryMs { get; set; }
    public string Payload { get; set; } = "";
}
```

The `Payload` payload supports multiline text: each line becomes one `payload`: line.

### Broadcast notifications to all clients

By default, every connection joins the `__all` room, so you can do:

```csharp
ServerSentEventsHub hub = null!;

server.UseServerSentEventsModule(sse => {
    hub = sse.Hub; // store for later
    //sse.AutoJoinRoom = "__all"; // default value for AutoJoinRoom (1)
});

// later anywhere (2)
await hub.BroadcastTextAsync("__all", "server is alive", @event: "notice");
```

### Keepalive

If KeepAliveInterval is enabled, it sends:

```
: ping\n\n
```

This helps :
- reverse proxies
- load balancers
- idle connection timeouts


## Example: Stream text App (Rooms + Broadcast)

::: code-group

```csharp [program.cs]
class Program {
    static async Task Main() {
        var server = new SimpleWServer(IPAddress.Any, 2015);

        // setup static files modules to server the index.html
        server.UseStaticFilesModule(options => {
            options.Path = @"C:\www\";
            options.Prefix = "/";
            options.CacheTimeout = TimeSpan.FromDays(1);
        });

        // setup sse
        server.UseServerSentEventsModule(opt => {
            opt.Prefix = "/sse";
            opt.KeepAliveInterval = TimeSpan.FromSeconds(15);

            hub = opt.Hub;

            opt.OnConnect = async (conn, ctx) => {
                await conn.SendEventAsync("connected", @event: "status");
            };
        });

        Console.WriteLine("server started at http://localhost:{server.Port}/");
        await server.RunAsync();
    }
}
```

```csharp [C:\www\index.html]
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>SimpleW SSE demo</title>
  <style>
    body { font-family: sans-serif; padding: 16px; }
    #log { white-space: pre-wrap; border: 1px solid #ccc; padding: 12px; border-radius: 8px; }
    .ok { color: green; }
    .ko { color: #b00020; }
  </style>
</head>
<body>
  <h1>SimpleW SSE demo</h1>
  <div id="status">Status: <span class="ko">disconnected</span></div>
  <h3>Messages</h3>
  <div id="log"></div>

  <script>
    const statusEl = document.querySelector("#status span");
    const logEl = document.getElementById("log");

    function log(msg) {
      logEl.textContent += msg + "\n";
      logEl.scrollTop = logEl.scrollHeight;
    }

    const es = new EventSource("/sse");

    es.onopen = () => {
      statusEl.textContent = "connected";
      statusEl.className = "ok";
      log("[open] connected");
    };

    es.onerror = () => {
      statusEl.textContent = "disconnected (retrying...)";
      statusEl.className = "ko";
      log("[error] disconnected (EventSource will retry automatically)");
    };

    // event "message" by default (if server does not send "event:")
    es.onmessage = (e) => {
      log(`[message] ${e.data}`);
    };

    // we send an event: tick
    es.addEventListener("tick", (e) => {
      log(`[tick] ${e.data}`);
    });

    // and event: status on connect
    es.addEventListener("status", (e) => {
      log(`[status] ${e.data}`);
    });
  </script>
</body>
</html>
```

:::