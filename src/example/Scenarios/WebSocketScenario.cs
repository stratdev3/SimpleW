using System.Collections.Concurrent;
using System.Text.Json;
using SimpleW;
using SimpleW.Modules;


namespace example;

internal sealed class WebSocketScenario : IScenario {

    private const string ChatPage = """
        <!doctype html>
        <html lang="en">

        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width,initial-scale=1" />
            <title>SimpleW - WebSocket Chat Test</title>
            <style>
                body {
                    font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
                    margin: 16px;
                }

                h1 {
                    margin: 0 0 12px;
                }

                .row {
                    display: flex;
                    gap: 8px;
                    align-items: center;
                    flex-wrap: wrap;
                    margin-bottom: 10px;
                }

                input[type="text"] {
                    padding: 8px;
                    width: min(520px, 100%);
                }

                button {
                    padding: 8px 12px;
                    cursor: pointer;
                }

                #chat {
                    margin-top: 10px;
                    border: 1px solid #ccc;
                    border-radius: 8px;
                    padding: 10px;
                    height: 55vh;
                    overflow: auto;
                    background: #fafafa;
                }

                .msg {
                    margin: 6px 0;
                    white-space: pre-wrap;
                    word-break: break-word;
                }

                .me {
                    color: #06c;
                }

                .sys {
                    color: #555;
                }

                .err {
                    color: #c00;
                }

                .small {
                    font-size: 12px;
                    color: #666;
                }

                .pill {
                    font-size: 12px;
                    padding: 2px 6px;
                    border: 1px solid #ddd;
                    border-radius: 999px;
                    background: #fff;
                    color: #333;
                }
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
                <input id="name" type="text" value="user1" />

                <label for="room"><b>Room</b></label>
                <input id="room" type="text" value="general" />

                <button id="btnJoin" disabled>Join</button>
                <button id="btnLeave" disabled>Leave</button>
                <button id="btnClear">Clear</button>
            </div>

            <div class="row">
                <label for="text"><b>Message</b></label>
                <input id="text" type="text" placeholder="type a message..." />
                <button id="btnSend" disabled>Send</button>
            </div>

            <div class="small">
                Protocol: send JSONs <code>{op,id?,payload}</code> with <code>op</code> to <code>chat/join</code>, <code>chat/leave</code>, <code>chat/msg</code>.
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
                        //ws = new WebSocket(url, ["bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NzEzNzA4MTMsImlhdCI6MTc2ODc3ODgxMywiaXNzIjoiaHR0cDovL2xvY2FsaG9zdDoyMDE1IiwiSWRlbnRpdHkiOnRydWUsIklkIjoiNGIwNjMxN2MtMWZhNC00YTIzLTgxZmYtNDFmMWViZjJkNjUzIiwiTG9naW4iOiJjLmNoYXRlYXVAc3RyYXRkZXYuZnIiLCJNYWlsIjoiYy5jaGF0ZWF1QHN0cmF0ZGV2LmZyIiwiRnVsbE5hbWUiOiJDSEFURUFVIENocmlzdG9waGUiLCJQcm9maWxlIjoiQWRtaW5pc3RyYXRldXIiLCJSb2xlcyI6WyJhZG1pbiIsImRlYnVnIl0sIlByZWZlcmVuY2VzIjpudWxsLCJSZWZyZXNoIjpmYWxzZX0.MtOgCuUPasT-4meoZl15F8zq4LkXxjKOhhcWsz25aJ0"]);
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
        """;

    public string Name => "websocket";

    public string Description => "Browser chat using WebSocket message routing and rooms.";

    public string Usage => string.Empty;

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        ConcurrentDictionary<Guid, ChatParticipant> participants = new();
        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/", (HttpSession session) => session.Response.Html(ChatPage));
        server.UseWebSocketModule(options => {
            options.Prefix = "/ws";
            options.AutoJoinRoom = null;

            options.Map("chat/join", async (connection, context, message) => {
                if (!message.TryDeserializePayload(out ChatMessage? chat) || chat == null || !chat.IsValid()) {
                    await connection.SendTextAsync(Error("Invalid chat/join payload.")).ConfigureAwait(false);
                    return;
                }

                participants[connection.Id] = new ChatParticipant(chat.room, chat.name);
                await context.JoinRoomAsync(chat.room, connection).ConfigureAwait(false);
                await context.Hub.BroadcastTextAsync(chat.room, Event("join", chat.room, chat.name, $"{chat.name} joined"), connection).ConfigureAwait(false);
                await connection.SendTextAsync(Event("join", chat.room, chat.name, $"joined {chat.room}")).ConfigureAwait(false);
            });

            options.Map("chat/leave", async (connection, context, message) => {
                if (!message.TryDeserializePayload(out ChatMessage? chat) || chat == null || !chat.IsValid()) {
                    await connection.SendTextAsync(Error("Invalid chat/leave payload.")).ConfigureAwait(false);
                    return;
                }

                await context.LeaveRoomAsync(chat.room, connection).ConfigureAwait(false);
                participants.TryRemove(connection.Id, out _);
                await context.Hub.BroadcastTextAsync(chat.room, Event("leave", chat.room, chat.name, $"{chat.name} left"), connection).ConfigureAwait(false);
            });

            options.Map("chat/msg", async (connection, context, message) => {
                if (!message.TryDeserializePayload(out ChatMessage? chat) || chat == null || !chat.IsValid()) {
                    await connection.SendTextAsync(Error("Invalid chat/msg payload.")).ConfigureAwait(false);
                    return;
                }
                await context.Hub.BroadcastTextAsync(chat.room, Event("msg", chat.room, chat.name, chat.text)).ConfigureAwait(false);
            });

            options.OnUnknown(async (connection, _, message) => {
                string detail = message.IsJson ? $"Unknown operation '{message.Op}'." : "Expected a JSON envelope.";
                await connection.SendTextAsync(Error(detail)).ConfigureAwait(false);
            });

            options.OnDisconnect = async (connection, context) => {
                if (participants.TryRemove(connection.Id, out ChatParticipant? participant)) {
                    await context.Hub.BroadcastTextAsync(participant.Room, Event("leave", participant.Room, participant.Name, $"{participant.Name} disconnected"), connection).ConfigureAwait(false);
                }
            };
        });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

    private static string Event(string kind, string room, string name, string? text) {
        return JsonSerializer.Serialize(new { op = "chat/event", payload = new { kind, room, name, text = text ?? string.Empty } });
    }

    private static string Error(string message) {
        return JsonSerializer.Serialize(new { op = "chat/error", payload = new { message } });
    }

    private sealed class ChatMessage {

        public string room { get; set; } = string.Empty;

        public string name { get; set; } = string.Empty;

        public string text { get; set; } = string.Empty;

        public bool IsValid() => !string.IsNullOrWhiteSpace(room) && !string.IsNullOrWhiteSpace(name);
    }

    private sealed record ChatParticipant(string Room, string Name);

}
