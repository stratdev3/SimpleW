# WebSockets

The WebsocketModule provides a the two-way communication channels over HTTP called Websockets : server can push data to the client without it has to request (except first time to connect socket).


## Key Concepts

### 1) A Single WebSocket Endpoint

The module exposes a **WS endpoint**, configured via `WebSocketOptions.Prefix` (default: `/ws`).

Even if you map `/ws/*`, the handler enforces **strict match**, so anything under `/ws/xxx` will return **404**.


### 2) Routing by op

Text messages are expected to be JSON and follow an envelope format:

```json
{ "op": "chat/join", "id": "42", "payload": { ... } }
```

- `op` : operation name used for routing (exact match)
- `id` : optional correlation id (string or number accepted)
- `payload` : optional JSON payload

If the incoming message is not JSON, it will be routed to the `OnUnknown` handler (if defined).


### 3) Hub and Rooms

The module includes an in-memory **WebSocketHub**, letting you :
- create rooms dynamically
- join / leave rooms
- broadcast text or binary messages to a room

Connections are **auto-removed on close**, and rooms are auto-cleaned when empty.


## Installation

Install the module using :

```csharp
server.UseWebSocketModule();
```

Or with options :

```csharp
server.UseWebSocketModule(ws => {
    ws.Prefix = "/ws";
});
```

See all [options](../reference/websocketmodule.md).


## Basic Usage

```csharp
using System.Net;
using SimpleW;
using SimpleW.Modules;

var server = new SimpleWServer(IPAddress.Any, 2015);

server.UseWebSocketModule(ws => {
    ws.Prefix = "/ws";

    ws.Map("ping", async (conn, ctx, msg) => {
        await conn.SendTextAsync("{\"op\":\"pong\"}");
    });

    ws.OnUnknown(async (conn, ctx, msg) => {
        await conn.SendTextAsync("unknown message");
    });
});

await server.RunAsync();
```

## Router API

### Map an operation

```csharp
ws.Map("chat/join", async (conn, ctx, msg) => {
    // ...
});
```

### Unknown / fallback

```csharp
ws.OnUnknown(async (conn, ctx, msg) => {
    await conn.SendTextAsync(msg.IsJson ? $"unknown op: {msg.Op}" : "bad message: expected JSON {op,payload}");
});
```

### Binary frames

```csharp
ws.OnBinary(async (conn, ctx, bytes) => {
    // handle raw bytes
});
```


## Example: Chat App (Rooms + Broadcast)

Below is a full example using :
- `chat/join`, `chat/leave`, `chat/msg`
- rooms via Hub
- broadcast with `except:` sender

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

        // setup websocket
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
    <title>SimpleW - WebSocket Chat Test</title>
    <style>
        body { font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 16px; }
        h1 { margin: 0 0 12px; }
        .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-bottom: 10px; }
        input[type="text"] { padding: 8px; width: min(520px, 100%); }
        button { padding: 8px 12px; cursor: pointer; }
        #chat { margin-top: 10px; border: 1px solid #ccc; border-radius: 8px; padding: 10px; height: 55vh; overflow: auto; background: #fafafa; }
        .msg { margin: 6px 0; white-space: pre-wrap; word-break: break-word;         }
        .me { color: #06c; }
        .sys { color: #555; }
        .err { color: #c00; }
        .small { font-size: 12px; color: #666; }
        .pill { font-size: 12px; padding: 2px 6px; border: 1px solid #ddd; border-radius: 999px; background: #fff;  color: #333; }
    </style>
</head>

<body>
    <h1>SimpleW WebSocket Chat Test</h1>

    <div class="row">
        <label for="wsUrl"><b>WS URL</b></label>
        <input id="wsUrl" type="text" value="" />
        <button id="btnConnect">Connect</button>
        <button id="btnDisconnect" disabled>Disconnect</button>
        <span id="status" class="pill">disconnected</span>
    </div>

    <div class="row">
        <label for="name"><b>Pseudo</b></label>
        <input id="name" type="text" value="me" />

        <label for="room"><b>Room</b></label>
        <input id="room" type="text" value="general" />

        <button id="btnJoin" disabled>Join</button>
        <button id="btnLeave" disabled>Leave</button>
        <button id="btnClear">Clear</button>
    </div>

    <div class="row">
        <label for="text"><b>Message</b></label>
        <input id="text" type="text" placeholder="Write a message..." />
        <button id="btnSend" disabled>Send</button>
    </div>

    <div class="small">
        Protocole: send JSON <code>{op,id?,payload}</code> with <code>op</code> to <code>chat/join</code>,
        <code>chat/leave</code>, <code>chat/msg</code>.
    </div>

    <div id="chat" aria-live="polite"></div>

    <script>
        const $ = (id) => document.getElementById(id);

        const chatEl = $("chat");
        const wsUrlEl = $("wsUrl");
        const nameEl = $("name");
        const roomEl = $("room");
        const textEl = $("text");

        const btnConnect = $("btnConnect");
        const btnDisconnect = $("btnDisconnect");
        const btnJoin = $("btnJoin");
        const btnLeave = $("btnLeave");
        const btnSend = $("btnSend");
        const btnClear = $("btnClear");

        const statusEl = $("status");

        let ws = null;

        function ts() {
            const d = new Date();
            return d.toLocaleTimeString();
        }

        function setStatus(text) {
            statusEl.textContent = text;
        }

        function addLine(text, cls = "sys") {
            const p = document.createElement("p");
            p.className = `msg ${cls}`;
            p.textContent = `[${ts()}] ${text}`;
            chatEl.appendChild(p);
            chatEl.scrollTop = chatEl.scrollHeight;
        }

        function setUiConnected(connected) {
            btnConnect.disabled = connected;
            btnDisconnect.disabled = !connected;
            btnJoin.disabled = !connected;
            btnLeave.disabled = !connected;
            btnSend.disabled = !connected;
            wsUrlEl.disabled = connected;
        }

        function defaultUrl() {
            if (location && location.host) {
                const proto = location.protocol === "https:" ? "wss" : "ws";
                return `${proto}://${location.host}/ws`;
            }
            return "ws://localhost:8080/ws";
        }

        wsUrlEl.value = defaultUrl();

        function sendEnvelope(op, payload) {
            if (!ws || ws.readyState !== WebSocket.OPEN) {
                addLine("Not connected.", "err");
                return;
            }
            const env = { op, payload: payload ?? {} };
            const txt = JSON.stringify(env);
            ws.send(txt);
            return txt;
        }

        function joinRoom() {
            const name = (nameEl.value || "anon").trim();
            const room = (roomEl.value || "general").trim();
            const raw = sendEnvelope("chat/join", { name, room });
            if (raw) addLine(`SEND join ${room} as ${name}`, "sys");
        }

        function leaveRoom() {
            const name = (nameEl.value || "anon").trim();
            const room = (roomEl.value || "general").trim();
            const raw = sendEnvelope("chat/leave", { name, room });
            if (raw) addLine(`SEND leave ${room}`, "sys");
        }

        function sendMsg() {
            const name = (nameEl.value || "anon").trim();
            const room = (roomEl.value || "general").trim();
            const text = (textEl.value || "").trim();
            if (!text) return;

            // show locally immediately
            addLine(`${name}@${room}: ${text}`, "me");

            const raw = sendEnvelope("chat/msg", { name, room, text });
            if (!raw) return;

            textEl.value = "";
            textEl.focus();
        }

        btnConnect.addEventListener("click", () => {
            const url = wsUrlEl.value.trim();
            if (!url) return;

            addLine(`Connecting to ${url} ...`, "sys");

            try {
                ws = new WebSocket(url);

                ws.addEventListener("open", () => {
                    setStatus("connected");
                    addLine("OPEN", "sys");
                    setUiConnected(true);
                    joinRoom();
                });

                ws.addEventListener("message", (ev) => {
                    if (typeof ev.data !== "string") {
                        addLine(`RECV (binary): ${Object.prototype.toString.call(ev.data)}`, "sys");
                        return;
                    }

                    // Try JSON (recommended)
                    try {
                        const obj = JSON.parse(ev.data);
                        const op = obj?.op;
                        const p = obj?.payload;

                        if (op === "chat/event" && p) {
                            const room = p.room ?? "?";
                            const name = p.name ?? "?";
                            const kind = p.kind ?? "event";
                            const text = p.text ?? "";
                            if (kind === "msg") {
                                addLine(`${name}@${room}: ${text}`, "sys");
                            } else {
                                addLine(`[${room}] ${text || kind}`, "sys");
                            }
                            return;
                        }

                        // fallback render
                        addLine(`RECV JSON: ${ev.data}`, "sys");
                    } catch {
                        // not JSON
                        addLine(`RECV: ${ev.data}`, "sys");
                    }
                });

                ws.addEventListener("close", (ev) => {
                    addLine(`CLOSE code=${ev.code} reason="${ev.reason}" wasClean=${ev.wasClean}`, "sys");
                    setStatus("disconnected");
                    setUiConnected(false);
                    ws = null;
                });

                ws.addEventListener("error", () => {
                    addLine("ERROR (check console + server logs)", "err");
                });

            } catch (e) {
                addLine(`Failed to connect: ${e}`, "err");
                ws = null;
            }
        });

        btnDisconnect.addEventListener("click", () => {
            if (!ws) return;
            addLine("Closing...", "sys");
            ws.close(1000, "client closing");
        });

        btnJoin.addEventListener("click", joinRoom);
        btnLeave.addEventListener("click", leaveRoom);

        btnSend.addEventListener("click", sendMsg);
        textEl.addEventListener("keydown", (e) => {
            if (e.key === "Enter") sendMsg();
        });

        btnClear.addEventListener("click", () => {
            chatEl.innerHTML = "";
        });
    </script>
</body>

</html>
```

:::


## Version enforcement

Only WebSocket version **13** is supported.
If not, server returns :
- HTTP **426 Upgrade Required**
- includes header `Sec-WebSocket-Version: 13` 


## Subprotocol

If `RequiredSubProtocol` is configured, it must match exactly an item in `Sec-WebSocket-Protocol`. Otherwise handshake fails with 400.
