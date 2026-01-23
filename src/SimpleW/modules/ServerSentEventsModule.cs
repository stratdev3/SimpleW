using System.Buffers;
using System.Collections.Concurrent;
using System.Text;


namespace SimpleW.Modules {

    /// <summary>
    /// Server Sent Events Module Extension
    /// </summary>
    public static class ServerSentEventsModuleExtension {

        /// <summary>
        /// Install Server Sent Events Module
        /// </summary>
        public static SimpleWServer UseServerSentEventsModule(this SimpleWServer server, Action<ServerSentEventsOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            ServerSentEventsOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new ServerSentEventsModule(options));
            return server;
        }

    }

    /// <summary>
    /// Server Sent Events Options
    /// </summary>
    public sealed class ServerSentEventsOptions {

        /// <summary>
        /// SSE endpoint path (default "/sse")
        /// </summary>
        public string Prefix { get; set; } = "/sse";

        /// <summary>
        /// Convenience CORS header: Access-Control-Allow-Origin: *
        /// (default false)
        /// </summary>
        public bool AllowAnyOrigin { get; set; } = false;

        /// <summary>
        /// If set, periodically send ": ping\n\n" to keep proxies/load balancers happy.=
        /// Default: 15 seconds.
        /// </summary>
        public TimeSpan? KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Automatically join every connection into this room.
        /// Default "__all". Set to null to disable.
        /// </summary>
        public string? AutoJoinRoom { get; set; } = "__all";

        /// <summary>
        /// Global hub to allow broadcasts to rooms (and/or an "all" room)
        /// You can store a reference to it and broadcast from anywhere
        /// </summary>
        public ServerSentEventsHub Hub { get; } = new();

        /// <summary>
        /// Called after the SSE handshake is done and transport ownership is taken
        /// </summary>
        public Func<ServerSentEventsConnection, ServerSentEventsContext, ValueTask>? OnConnect { get; set; }

        /// <summary>
        /// Called when the connection is closed
        /// </summary>
        public Func<ServerSentEventsConnection, ServerSentEventsContext, ValueTask>? OnDisconnect { get; set; }

        /// <summary>
        /// Validate + normalize
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        internal ServerSentEventsOptions ValidateAndNormalize() {
            Prefix = SimpleWExtension.NormalizePrefix(Prefix);
            if (Prefix == "/") {
                throw new ArgumentException("ServerSentEventsOptions.Prefix must not be '/' (choose e.g. '/sse').", nameof(Prefix));
            }
            if (KeepAliveInterval.HasValue && KeepAliveInterval.Value <= TimeSpan.Zero) {
                KeepAliveInterval = null;
            }
            if (string.IsNullOrWhiteSpace(AutoJoinRoom)) {
                AutoJoinRoom = null;
            }
            return this;
        }

        internal string PrefixWildCard => Prefix + "/*";
    }

    /// <summary>
    /// Module: SSE endpoint
    /// </summary>
    internal sealed class ServerSentEventsModule : IHttpModule {

        /// <summary>
        /// ServerSentEventsOptions
        /// </summary>
        private readonly ServerSentEventsOptions _options;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public ServerSentEventsModule(ServerSentEventsOptions options) {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Install(SimpleWServer server) {
            if (server.IsStarted) {
                throw new InvalidOperationException("ServerSentEventsModule must be installed before server start.");
            }

            // accept prefix
            server.MapGet(_options.Prefix, (HttpSession session) => HandlerAsync(session));
            // accept prefix wildcard as fallback (but will enforce exact match)
            server.MapGet(_options.PrefixWildCard, (HttpSession session) => HandlerAsync(session));
        }

        /// <summary>
        /// Handler
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        private async ValueTask HandlerAsync(HttpSession session) {

            string reqPath = SimpleWExtension.NormalizePrefix(session.Request.Path);

            // strict: only exact Prefix, everything else under /sse/* returns 404
            if (!string.Equals(reqPath, _options.Prefix, StringComparison.Ordinal)) {
                await session.Response.Status(404).Text("Not Found").SendAsync().ConfigureAwait(false);
                return;
            }

            if (!string.Equals(session.Request.Method, "GET", StringComparison.OrdinalIgnoreCase)) {
                await session.Response.Status(405).Text("Method Not Allowed").SendAsync().ConfigureAwait(false);
                return;
            }

            // SSE handshake
            session.Response
                   .Status(200)
                   .NoCompression()
                   .NoContentLength()
                   .AddHeader("Content-Type", "text/event-stream; charset=utf-8")
                   .AddHeader("Cache-Control", "no-cache")
                   .AddHeader("Connection", "keep-alive")
                   .AddHeader("X-Accel-Buffering", "no");

            if (_options.AllowAnyOrigin) {
                session.Response.AddHeader("Access-Control-Allow-Origin", "*");
            }

            // send headers now
            await session.Response.SendAsync().ConfigureAwait(false);

            // take transport ownership (stop HttpSession loop)
            if (!session.TryTakeTransportOwnership()) {
                try {
                    session.Dispose();
                }
                catch { }
                return;
            }

            ServerSentEventsConnection conn = new(session) { Hub = _options.Hub };
            ServerSentEventsContext ctx = new(session, _options.Hub, reqPath);

            try {
                // auto-join room (default "__all")
                if (!string.IsNullOrWhiteSpace(_options.AutoJoinRoom)) {
                    // also plugs conn.HubBroadcast* for this room
                    await conn.JoinAsync(_options.AutoJoinRoom!).ConfigureAwait(false);
                }

                // send comment for proxy
                await conn.SendCommentAsync("connected").ConfigureAwait(false);

                if (_options.OnConnect is not null) {
                    await _options.OnConnect(conn, ctx).ConfigureAwait(false);
                }

                // keepalive loop
                if (_options.KeepAliveInterval.HasValue) {
                    await conn.RunKeepAliveLoopAsync(_options.KeepAliveInterval.Value).ConfigureAwait(false);
                }

                // if no keepalive, just wait for closure
                await conn.WaitForCloseAsync().ConfigureAwait(false);
            }
            catch { }
            finally {
                try {
                    if (_options.OnDisconnect is not null) {
                        await _options.OnDisconnect(conn, ctx).ConfigureAwait(false);
                    }
                }
                catch { }

                // ensure closed
                conn.Close();
            }
        }
    }

    /// <summary>
    /// Public SSE connection wrapper.
    /// You can send events + join/leave rooms via Hub.
    /// </summary>
    public sealed class ServerSentEventsConnection : IDisposable {

        /// <summary>
        /// Guid of the current ServerSentEventsConnection
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// The underlying HttpSession
        /// </summary>
        private readonly HttpSession _session;

        /// <summary>
        /// Hub
        /// </summary>
        internal ServerSentEventsHub? Hub { get; set; }

        /// <summary>
        /// Optional bag
        /// </summary>
        public object? Tag { get; set; }

        private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
        private int _closed;
        private readonly TaskCompletionSource _tcsClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Closed
        /// </summary>
        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        /// <summary>
        /// Closed
        /// </summary>
        public event Action<ServerSentEventsConnection>? Closed;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="session"></param>
        internal ServerSentEventsConnection(HttpSession session) {
            _session = session;
        }

        /// <summary>
        /// Join a room (Hub must be set by module)
        /// </summary>
        public ValueTask JoinAsync(string room) {
            if (Hub is null) {
                throw new InvalidOperationException("No hub attached to this connection.");
            }
            return Hub.JoinAsync(room, this);
        }

        /// <summary>
        /// Leave a room (Hub must be set by module)
        /// </summary>
        public ValueTask LeaveAsync(string room) {
            if (Hub is null) {
                throw new InvalidOperationException("No hub attached to this connection.");
            }
            return Hub.LeaveAsync(room, this);
        }

        /// <summary>
        /// Send SSE comment line (": ...\n\n")
        /// </summary>
        public ValueTask SendCommentAsync(string? comment = null) {
            if (IsClosed) {
                return ValueTask.CompletedTask;
            }
            if (string.IsNullOrEmpty(comment)) {
                return SendUtf8Async(": ping\n\n");
            }
            return SendUtf8Async($": {comment}\n\n");
        }

        /// <summary>
        /// Send SSE event (data + optional event/id/retry).
        /// </summary>
        public ValueTask SendEventAsync(string data, string? @event = null, string? id = null, int? retryMs = null) {
            return SendAsync(
                new ServerSentEventsMessage {
                    Payload = data ?? "",
                    Event = @event,
                    Id = id,
                    RetryMs = retryMs
                }
            );
        }

        /// <summary>
        /// Send SSE message object
        /// </summary>
        public ValueTask SendAsync(ServerSentEventsMessage message) {
            if (IsClosed) {
                return ValueTask.CompletedTask;
            }
            return SendUtf8Async(FormatSse(message));
        }

        /// <summary>
        /// Send string
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private async ValueTask SendUtf8Async(string text) {
            if (IsClosed) {
                return;
            }

            int max = Utf8NoBom.GetMaxByteCount(text.Length);
            byte[] buf = _pool.Rent(max);
            try {
                int len = Utf8NoBom.GetBytes(text.AsSpan(), buf.AsSpan());
                await _session.SendAsync(new ReadOnlyMemory<byte>(buf, 0, len)).ConfigureAwait(false);
            }
            catch {
                // On any write error => consider dead
                Close();
            }
            finally {
                _pool.Return(buf);
            }
        }

        /// <summary>
        /// KeepAlive Loop
        /// </summary>
        /// <param name="interval"></param>
        /// <returns></returns>
        internal async ValueTask RunKeepAliveLoopAsync(TimeSpan interval) {
            while (!IsClosed) {
                try {
                    await Task.Delay(interval).ConfigureAwait(false);
                }
                catch {
                    // ignore
                }
                if (IsClosed)
                    break;
                await SendCommentAsync("ping").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waiter
        /// </summary>
        /// <returns></returns>
        public Task WaitForCloseAsync() => _tcsClosed.Task;

        /// <summary>
        /// Close Sse
        /// </summary>
        public void Close() {
            if (Interlocked.Exchange(ref _closed, 1) != 0) {
                return;
            }
            try {
                Closed?.Invoke(this);
            }
            catch { }
            _tcsClosed.TrySetResult();
            try {
                _session.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() => Close();

        #region helpers

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static string FormatSse(ServerSentEventsMessage message) {
            // normalize CRLF -> LF
            string data = (message.Payload ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

            StringBuilder sb = new();

            if (!string.IsNullOrWhiteSpace(message.Id)) {
                sb.Append("id: ").Append(message.Id).Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(message.Event)) {
                sb.Append("event: ").Append(message.Event).Append('\n');
            }
            if (message.RetryMs.HasValue && message.RetryMs.Value >= 0) {
                sb.Append("retry: ").Append(message.RetryMs.Value).Append('\n');
            }

            // SSE wants one "data:" line per line of payload
            if (data.Length == 0) {
                sb.Append("data:").Append('\n');
            }
            else {
                int start = 0;
                while (true) {
                    int idx = data.IndexOf('\n', start);
                    if (idx < 0) {
                        sb.Append("data: ").Append(data.AsSpan(start)).Append('\n');
                        break;
                    }
                    sb.Append("data: ").Append(data.AsSpan(start, idx - start)).Append('\n');
                    start = idx + 1;
                    if (start >= data.Length) {
                        // trailing newline => add empty data line
                        sb.Append("data:").Append('\n');
                        break;
                    }
                }
            }

            sb.Append('\n'); // end of event
            return sb.ToString();
        }

        #endregion helpers

    }

    /// <summary>
    /// Context passed to handlers
    /// </summary>
    public sealed class ServerSentEventsContext {

        /// <summary>
        /// The underlying HttpSession
        /// </summary>
        public HttpSession Session { get; }

        /// <summary>
        /// Hub attached to the module
        /// </summary>
        public ServerSentEventsHub Hub { get; }

        /// <summary>
        /// Request path (normalized)
        /// </summary>
        public string RequestPath { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="session"></param>
        /// <param name="hub"></param>
        /// <param name="requestPath"></param>
        internal ServerSentEventsContext(HttpSession session, ServerSentEventsHub hub, string requestPath) {
            Session = session;
            Hub = hub;
            RequestPath = requestPath;
        }

    }

    /// <summary>
    /// Rooms hub for SSE connections
    /// </summary>
    public sealed class ServerSentEventsHub {

        /// <summary>
        /// Rooms
        /// </summary>
        private readonly ConcurrentDictionary<string, ServerSentEventsRoom> _rooms = new(StringComparer.Ordinal);

        /// <summary>
        /// Join a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="conn"></param>
        /// <returns></returns>
        public ValueTask JoinAsync(string room, ServerSentEventsConnection conn) {
            if (string.IsNullOrWhiteSpace(room)) {
                throw new ArgumentException("room must not be empty.", nameof(room));
            }
            ArgumentNullException.ThrowIfNull(conn);

            ServerSentEventsRoom? r = _rooms.GetOrAdd(room, static _ => new ServerSentEventsRoom());
            r?.Add(conn);

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Leave a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="conn"></param>
        /// <returns></returns>
        public ValueTask LeaveAsync(string room, ServerSentEventsConnection conn) {
            ArgumentNullException.ThrowIfNull(conn);

            if (_rooms.TryGetValue(room, out ServerSentEventsRoom? r)) {
                r.Remove(conn);
                if (r.Count == 0) {
                    _rooms.TryRemove(room, out _);
                }
            }
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Broadcast to one room
        /// </summary>
        public ValueTask BroadcastAsync(string room, ServerSentEventsMessage message, ServerSentEventsConnection? except = null) {
            if (_rooms.TryGetValue(room, out ServerSentEventsRoom? r)) {
                return r.BroadcastAsync(message, except);
            }
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Broadcast text with optional event name
        /// </summary>
        public ValueTask BroadcastTextAsync(string room, string data, string? @event = "message", ServerSentEventsConnection? except = null) {
            return BroadcastAsync(room, new ServerSentEventsMessage { Payload = data ?? "", Event = @event }, except);
        }

        /// <summary>
        /// Broadcast to multiple rooms (no dedup)
        /// </summary>
        public async ValueTask BroadcastManyAsync(IEnumerable<string> rooms, ServerSentEventsMessage message, ServerSentEventsConnection? except = null) {
            foreach (string room in rooms) {
                await BroadcastAsync(room, message, except).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// One room = set of live connections
        /// </summary>
        private sealed class ServerSentEventsRoom {

            /// <summary>
            /// connections
            /// </summary>
            private readonly ConcurrentDictionary<Guid, ServerSentEventsConnection> _conns = new();

            /// <summary>
            /// Count
            /// </summary>
            public int Count => _conns.Count;

            /// <summary>
            /// Add a connection
            /// </summary>
            /// <param name="conn"></param>
            public void Add(ServerSentEventsConnection conn) {
                _conns[conn.Id] = conn;
                conn.Closed += OnConnClosed;
            }

            /// <summary>
            /// Remove a connection
            /// </summary>
            /// <param name="conn"></param>
            public void Remove(ServerSentEventsConnection conn) {
                _conns.TryRemove(conn.Id, out _);
                conn.Closed -= OnConnClosed;
            }

            /// <summary>
            /// Connection Closed callback
            /// </summary>
            /// <param name="conn"></param>
            private void OnConnClosed(ServerSentEventsConnection conn) {
                _conns.TryRemove(conn.Id, out _);
                conn.Closed -= OnConnClosed;
            }

            /// <summary>
            /// Send a Broadcast
            /// </summary>
            /// <param name="message"></param>
            /// <param name="except"></param>
            /// <returns></returns>
            public async ValueTask BroadcastAsync(ServerSentEventsMessage message, ServerSentEventsConnection? except = null) {
                foreach (var kv in _conns) {
                    ServerSentEventsConnection c = kv.Value;
                    if (c.IsClosed) {
                        _conns.TryRemove(kv.Key, out _);
                        continue;
                    }
                    if (except != null && ReferenceEquals(c, except)) {
                        continue;
                    }
                    await c.SendAsync(message).ConfigureAwait(false);
                }
            }

        }

    }

    /// <summary>
    /// Public SSE message (frames builder)
    /// </summary>
    public sealed class ServerSentEventsMessage {

        /// <summary>
        /// Message id (optional)
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Event name (optional). If null => default "message" on browser EventSource.
        /// </summary>
        public string? Event { get; set; }

        /// <summary>
        /// Client retry in ms (optional)
        /// </summary>
        public int? RetryMs { get; set; }

        /// <summary>
        /// Payload (can be multiline)
        /// </summary>
        public string Payload { get; set; } = "";

    }

}
