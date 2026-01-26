using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;


namespace SimpleW.Modules {

    /// <summary>
    /// WebSocketModuleExtension
    /// </summary>
    public static class WebSocketModuleExtension {

        /// <summary>
        /// Install WebSocket Module (raw RFC6455, no System.Net.WebSockets)
        /// </summary>
        public static SimpleWServer UseWebSocketModule(this SimpleWServer server, Action<WebSocketOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            WebSocketOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new WebSocketModule(options));
            return server;
        }
    }

    /// <summary>
    /// WebSocket Options
    /// - single endpoint (Prefix)
    /// - message routing by "op" (exact match)
    /// - optional hub for broadcast
    /// </summary>
    public sealed class WebSocketOptions {

        /// <summary>
        /// Single WS endpoint path (default "/ws")
        /// </summary>
        public string Prefix { get; set; } = "/ws";

        /// <summary>
        /// Max message size after reassembly (default 1 MiB)
        /// </summary>
        public int MaxMessageBytes { get; set; } = 1 * 1024 * 1024;

        /// <summary>
        /// If set, must be present in Sec-WebSocket-Protocol
        /// </summary>
        public string? RequiredSubProtocol { get; set; }

        /// <summary>
        /// If set, periodically send ping to keep proxies/load balancers happy
        /// </summary>
        public TimeSpan? KeepAliveInterval { get; set; }

        /// <summary>
        /// Global hub to allow broadcasts to rooms (and/or an "all" room)
        /// You can store a reference to it and broadcast from anywhere
        /// </summary>
        public WebSocketHub Hub { get; } = new();

        /// <summary>
        /// Automatically join every connection into this room.
        /// Handy to broadcast to everyone: options.Hub.BroadcastTextAsync("__all", ...)
        /// Default "__all". Set to null to disable.
        /// </summary>
        public string? AutoJoinRoom { get; set; } = "__all";

        /// <summary>
        /// Message router (exact match on msg.Op)
        /// </summary>
        public WebSocketRouter Router { get; } = new();

        /// <summary>
        /// Called after successful handshake, before the router
        /// (Typical: auth handshake, join rooms...)
        /// </summary>
        public Func<WebSocketConnection, WebSocketContext, ValueTask>? OnConnect { get; set; }

        /// <summary>
        /// Called when the connection is closed (finally)
        /// </summary>
        public Func<WebSocketConnection, WebSocketContext, ValueTask>? OnDisconnect { get; set; }

        /// <summary>
        /// Register a handler for an op
        /// </summary>
        /// <param name="op"></param>
        /// <param name="handler"></param>
        public void Map(string op, WebSocketMessageHandler handler) => Router.Map(op, handler);

        /// <summary>
        /// Register fallback handler
        /// </summary>
        /// <param name="handler"></param>
        public void OnUnknown(WebSocketMessageHandler handler) => Router.OnUnknown(handler);

        /// <summary>
        /// Binary handler
        /// </summary>
        public void OnBinary(Func<WebSocketConnection, WebSocketContext, ReadOnlyMemory<byte>, ValueTask> handler) => Router.OnBinary(handler);

        /// <summary>
        /// Validate + normalize
        /// </summary>
        internal WebSocketOptions ValidateAndNormalize() {
            Prefix = SimpleWExtension.NormalizePrefix(Prefix);
            if (Prefix == "/") {
                throw new ArgumentException("WebSocketOptions.Prefix must not be '/' (choose e.g. '/ws').", nameof(Prefix));
            }
            if (MaxMessageBytes <= 0) {
                MaxMessageBytes = 1 * 1024 * 1024;
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
    /// Module: single endpoint + message routing
    /// </summary>
    internal sealed class WebSocketModule : IHttpModule {

        /// <summary>
        /// WebSocketOptions
        /// </summary>
        private readonly WebSocketOptions _options;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public WebSocketModule(WebSocketOptions options) {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Install(SimpleWServer server) {
            if (server.IsStarted) {
                throw new InvalidOperationException("WebSocketModule must be installed before server start.");
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

            // strict: only exact Prefix, everything else under /ws/* returns 404
            if (!string.Equals(reqPath, _options.Prefix, StringComparison.Ordinal)) {
                await session.Response.Status(404).Text("Not Found").SendAsync().ConfigureAwait(false);
                return;
            }

            if (!IsWebSocketUpgrade(session.Request, out string? wsKey, out string? wsVersion, out string? wsProtocols)) {
                await session.Response.Status(400).Text("Bad Request (expected WebSocket upgrade)").SendAsync().ConfigureAwait(false);
                return;
            }

            if (!string.Equals(wsVersion, "13", StringComparison.Ordinal)) {
                await session.Response
                             .Status(426)
                             .AddHeader("Sec-WebSocket-Version", "13")
                             .AddHeader("Connection", "close")
                             .Text("Upgrade Required")
                             .SendAsync()
                             .ConfigureAwait(false);
                return;
            }

            // subprotocol selection
            string? selectedProtocol = null;
            if (!string.IsNullOrWhiteSpace(_options.RequiredSubProtocol)) {
                if (!ContainsProtocol(wsProtocols, _options.RequiredSubProtocol!)) {
                    await session.Response
                                 .Status(400)
                                 .AddHeader("Connection", "close")
                                 .Text("Bad Request (subprotocol mismatch)")
                                 .SendAsync()
                                 .ConfigureAwait(false);
                    return;
                }
                selectedProtocol = _options.RequiredSubProtocol;
            }

            string accept = ComputeWebSocketAccept(wsKey!);

            session.Response
                   .Status(101)
                   .NoContentLength()
                   .AddHeader("Connection", "Upgrade")
                   .AddHeader("Upgrade", "websocket")
                   .AddHeader("Sec-WebSocket-Accept", accept);

            if (!string.IsNullOrWhiteSpace(selectedProtocol)) {
                session.Response.AddHeader("Sec-WebSocket-Protocol", selectedProtocol);
            }

            await session.Response.SendAsync().ConfigureAwait(false);

            if (!session.TryTakeTransportOwnership()) {
                try {
                    session.Dispose();
                }
                catch { }
                return;
            }

            WebSocketConnection conn = new(session, _options.MaxMessageBytes, _options.KeepAliveInterval);
            WebSocketContext ctx = new(session, _options.Hub, reqPath);

            try {
                // auto-join room (default "__all")
                if (!string.IsNullOrWhiteSpace(_options.AutoJoinRoom)) {
                    // also plugs conn.HubBroadcast* for this room
                    await _options.Hub.JoinWithBroadcastAsync(_options.AutoJoinRoom!, conn).ConfigureAwait(false);
                }

                if (_options.OnConnect != null) {
                    await _options.OnConnect(conn, ctx).ConfigureAwait(false);
                }

                await _options.Router.RunAsync(conn, ctx).ConfigureAwait(false);
            }
            catch {
                try {
                    await conn.CloseAsync(1011, "server error").ConfigureAwait(false);
                }
                catch { }
            }
            finally {
                try {
                    if (_options.OnDisconnect != null) {
                        await _options.OnDisconnect(conn, ctx).ConfigureAwait(false);
                    }
                }
                catch { }

                try {
                    session.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// IsWebSocketUpgrade
        /// </summary>
        /// <param name="req"></param>
        /// <param name="wsKey"></param>
        /// <param name="wsVersion"></param>
        /// <param name="wsProtocols"></param>
        /// <returns></returns>
        private static bool IsWebSocketUpgrade(HttpRequest req, out string? wsKey, out string? wsVersion, out string? wsProtocols) {
            wsKey = null;
            wsVersion = null;
            wsProtocols = null;

            if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (!req.Headers.TryGetValue("Upgrade", out var upgrade)
                || !string.Equals(upgrade?.Trim(), "websocket", StringComparison.OrdinalIgnoreCase)
            ) {
                return false;
            }
            if (!req.Headers.TryGetValue("Connection", out var conn)
                || conn == null || conn.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) < 0
            ) {
                return false;
            }

            if (!req.Headers.TryGetValue("Sec-WebSocket-Key", out wsKey) || string.IsNullOrWhiteSpace(wsKey)) {
                return false;
            }

            req.Headers.TryGetValue("Sec-WebSocket-Version", out wsVersion);
            req.Headers.TryGetValue("Sec-WebSocket-Protocol", out wsProtocols);
            return true;
        }

        /// <summary>
        /// ContainsProtocol
        /// </summary>
        /// <param name="header"></param>
        /// <param name="required"></param>
        /// <returns></returns>
        private static bool ContainsProtocol(string? header, string required) {
            if (string.IsNullOrWhiteSpace(header)) {
                return false;
            }
            foreach (string item in header.Split(',')) {
                if (string.Equals(item.Trim(), required, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ComputeWebSocketAccept
        /// </summary>
        /// <param name="secWebSocketKey"></param>
        /// <returns></returns>
        private static string ComputeWebSocketAccept(string secWebSocketKey) {
            const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
            byte[] input = Encoding.ASCII.GetBytes(secWebSocketKey.Trim() + Guid);
            byte[] hash = SHA1.HashData(input);
            return Convert.ToBase64String(hash);
        }

    }

    /// <summary>
    /// WebSocketConnection (raw frames) + hub
    /// </summary>
    public sealed class WebSocketConnection {

        /// <summary>
        /// Guid of the current WebSocketConnection
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <summary>
        /// The underlying HttpSession
        /// </summary>
        private readonly HttpSession _session;

        private readonly int _maxMessageBytes;
        private readonly TimeSpan? _pingInterval;

        private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
        private int _closed;

        /// <summary>
        /// Closed
        /// </summary>
        public event Action? Closed;

        // optional (set by hub endpoint)
        private Func<string, WebSocketConnection?, ValueTask>? _broadcastText;
        private Func<ReadOnlyMemory<byte>, WebSocketConnection?, ValueTask>? _broadcastBinary;

        /// <summary>
        /// Remote Endpoint
        /// </summary>
        public EndPoint? RemoteEndPoint => _session.Socket.RemoteEndPoint;

        /// <summary>
        /// Constructor
        /// </summary>
        public WebSocketConnection(HttpSession session, int maxMessageBytes, TimeSpan? pingInterval) {
            _session = session;
            _maxMessageBytes = maxMessageBytes;
            _pingInterval = pingInterval;
        }

        #region hub broadcast

        /// <summary>
        /// SetHubBroadcasters
        /// </summary>
        /// <param name="broadcastText"></param>
        /// <param name="broadcastBinary"></param>
        internal void SetHubBroadcasters(Func<string, WebSocketConnection?, ValueTask> broadcastText, Func<ReadOnlyMemory<byte>, WebSocketConnection?, ValueTask> broadcastBinary) {
            _broadcastText = broadcastText;
            _broadcastBinary = broadcastBinary;
        }

        /// <summary>
        /// HubBroadcastTextAsync
        /// </summary>
        /// <param name="text"></param>
        /// <param name="except"></param>
        /// <returns></returns>
        public ValueTask HubBroadcastTextAsync(string text, WebSocketConnection? except = null) {
            if (_broadcastText == null) {
                return ValueTask.CompletedTask;
            }
            return _broadcastText(text, except);
        }

        /// <summary>
        /// HubBroadcastBinaryAsync
        /// </summary>
        /// <param name="data"></param>
        /// <param name="except"></param>
        /// <returns></returns>
        public ValueTask HubBroadcastBinaryAsync(ReadOnlyMemory<byte> data, WebSocketConnection? except = null) {
            if (_broadcastBinary == null) {
                return ValueTask.CompletedTask;
            }
            return _broadcastBinary(data, except);
        }

        #endregion hub broadcast

        #region send

        /// <summary>
        /// SendTextAsync
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public ValueTask SendTextAsync(string text) {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            return SendFrameAsync(opcode: 0x1, payload);
        }

        /// <summary>
        /// SendBinaryAsync
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> payload) => SendFrameAsync(opcode: 0x2, payload);

        /// <summary>
        /// PingAsync
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public ValueTask PingAsync(ReadOnlyMemory<byte> payload = default) => SendFrameAsync(opcode: 0x9, payload);

        /// <summary>
        /// CloseAsync
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        /// <returns></returns>
        public async ValueTask CloseAsync(ushort code = 1000, string? reason = null) {
            if (Interlocked.Exchange(ref _closed, 1) != 0) {
                return;
            }

            byte[] reasonBytes = string.IsNullOrEmpty(reason) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(reason);
            int len = 2 + reasonBytes.Length;

            byte[] buf = _pool.Rent(len);
            try {
                buf[0] = (byte)(code >> 8);
                buf[1] = (byte)(code & 0xFF);
                if (reasonBytes.Length > 0) {
                    Buffer.BlockCopy(reasonBytes, 0, buf, 2, reasonBytes.Length);
                }

                await SendFrameAsync(opcode: 0x8, buf.AsMemory(0, len)).ConfigureAwait(false);
            }
            finally {
                _pool.Return(buf);
                FireClosed();
            }
        }

        /// <summary>
        /// FireClosed
        /// </summary>
        private void FireClosed() {
            try {
                Closed?.Invoke();
            }
            catch { }
        }

        #endregion send

        #region read messages

        /// <summary>
        /// ReadMessagesAsync
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public async IAsyncEnumerable<WebSocketMessage> ReadMessagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) {
            byte[]? reassembly = null;
            int reassemblyLen = 0;
            byte reassemblyOpcode = 0;

            using CancellationTokenSource pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? pingTask = null;
            if (_pingInterval.HasValue) {
                pingTask = RunPingLoopAsync(_pingInterval.Value, pingCts.Token);
            }

            try {
                while (!ct.IsCancellationRequested) {
                    Frame frame = await ReadFrameAsync(ct).ConfigureAwait(false);
                    if (frame.Kind == FrameKind.EOF) {
                        yield break;
                    }

                    if (frame.Opcode == 0x9) { // ping -> pong
                        await SendFrameAsync(0xA, frame.Payload).ConfigureAwait(false);
                        frame.Dispose();
                        continue;
                    }
                    if (frame.Opcode == 0xA) { // pong
                        frame.Dispose();
                        continue;
                    }
                    if (frame.Opcode == 0x8) { // close
                        frame.Dispose();
                        FireClosed();
                        yield break;
                    }

                    // fragmentation
                    bool fin = frame.Fin;
                    byte opcode = frame.Opcode;

                    if (opcode == 0x0) {
                        // continuation
                        if (reassembly == null) {
                            frame.Dispose();
                            continue; // ignore
                        }
                        if (reassemblyLen + frame.Payload.Length > _maxMessageBytes) {
                            frame.Dispose();
                            await CloseAsync(1009, "message too big").ConfigureAwait(false);
                            yield break;
                        }

                        frame.Payload.CopyTo(reassembly.AsMemory(reassemblyLen));
                        reassemblyLen += frame.Payload.Length;
                        frame.Dispose();

                        if (fin) {
                            // emit
                            if (reassemblyOpcode == 0x1) {
                                string text = Encoding.UTF8.GetString(reassembly, 0, reassemblyLen);
                                yield return new WebSocketMessage(WebSocketMessageKind.Text, text, default);
                            }
                            else if (reassemblyOpcode == 0x2) {
                                yield return new WebSocketMessage(WebSocketMessageKind.Binary, null, reassembly.AsMemory(0, reassemblyLen));
                            }

                            reassembly = null;
                            reassemblyLen = 0;
                            reassemblyOpcode = 0;
                        }

                        continue;
                    }

                    if (opcode is not (0x1 or 0x2)) {
                        frame.Dispose();
                        continue;
                    }

                    if (fin) {
                        // single frame message
                        if (opcode == 0x1) {
                            string text = Encoding.UTF8.GetString(frame.Payload.Span);
                            frame.Dispose();
                            yield return new WebSocketMessage(WebSocketMessageKind.Text, text, default);
                        }
                        else {
                            ReadOnlyMemory<byte> data = frame.Payload;
                            frame.Dispose();
                            yield return new WebSocketMessage(WebSocketMessageKind.Binary, null, data);
                        }
                        continue;
                    }

                    // start reassembly
                    if (frame.Payload.Length > _maxMessageBytes) {
                        frame.Dispose();
                        await CloseAsync(1009, "message too big").ConfigureAwait(false);
                        yield break;
                    }

                    reassembly = _pool.Rent(_maxMessageBytes);
                    reassemblyOpcode = opcode;
                    reassemblyLen = frame.Payload.Length;
                    frame.Payload.CopyTo(reassembly);
                    frame.Dispose();
                }
            }
            finally {
                if (reassembly != null) {
                    _pool.Return(reassembly);
                }

                try {
                    pingCts.Cancel();
                }
                catch { }

                if (pingTask != null) {
                    try { await pingTask.ConfigureAwait(false); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// RunPingLoopAsync
        /// </summary>
        /// <param name="interval"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task RunPingLoopAsync(TimeSpan interval, CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                try {
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                    if (ct.IsCancellationRequested) {
                        return;
                    }
                    await PingAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch {
                    // ignore
                }
            }
        }

        #endregion read messages

        #region frame write

        /// <summary>
        /// Frame
        /// </summary>
        private readonly struct Frame {

            public readonly FrameKind Kind;
            public readonly bool Fin;
            public readonly byte Opcode;
            public readonly ReadOnlyMemory<byte> Payload;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="kind"></param>
            /// <param name="fin"></param>
            /// <param name="opcode"></param>
            /// <param name="payload"></param>
            public Frame(FrameKind kind, bool fin, byte opcode, ReadOnlyMemory<byte> payload) {
                Kind = kind;
                Fin = fin;
                Opcode = opcode;
                Payload = payload;
            }

            /// <summary>
            /// Dispose
            /// </summary>
            public void Dispose() {
                // no-op, payload is already pooled in our implementation
            }
        }

        /// <summary>
        /// Frame Kind
        /// </summary>
        private enum FrameKind : byte {
            Data = 1,
            EOF = 2
        }

        /// <summary>
        /// SendFrameAsync
        /// </summary>
        /// <param name="opcode"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        private async ValueTask SendFrameAsync(byte opcode, ReadOnlyMemory<byte> payload) {
            if (Volatile.Read(ref _closed) != 0) {
                return;
            }

            Stream s = _session.TransportStream;

            int payloadLen = payload.Length;
            int headerLen = 2;
            if (payloadLen >= 126 && payloadLen <= 65535) {
                headerLen += 2;
            }
            else if (payloadLen > 65535) {
                headerLen += 8;
            }

            byte[] header = _pool.Rent(headerLen);
            try {
                header[0] = (byte)(0x80 | (opcode & 0x0F)); // FIN=1

                if (payloadLen < 126) {
                    header[1] = (byte)payloadLen;
                }
                else if (payloadLen <= 65535) {
                    header[1] = 126;
                    header[2] = (byte)((payloadLen >> 8) & 0xFF);
                    header[3] = (byte)(payloadLen & 0xFF);
                }
                else {
                    header[1] = 127;
                    ulong len = (ulong)payloadLen;
                    header[2] = (byte)((len >> 56) & 0xFF);
                    header[3] = (byte)((len >> 48) & 0xFF);
                    header[4] = (byte)((len >> 40) & 0xFF);
                    header[5] = (byte)((len >> 32) & 0xFF);
                    header[6] = (byte)((len >> 24) & 0xFF);
                    header[7] = (byte)((len >> 16) & 0xFF);
                    header[8] = (byte)((len >> 8) & 0xFF);
                    header[9] = (byte)(len & 0xFF);
                }

                await s.WriteAsync(header.AsMemory(0, headerLen)).ConfigureAwait(false);
                if (payloadLen > 0) {
                    await s.WriteAsync(payload).ConfigureAwait(false);
                }
                await s.FlushAsync().ConfigureAwait(false);
            }
            finally {
                _pool.Return(header);
            }
        }

        #endregion frame write

        #region frame read

        /// <summary>
        /// ReadFrameAsync
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask<Frame> ReadFrameAsync(CancellationToken ct) {
            Stream s = _session.TransportStream;

            byte[] header = _pool.Rent(2);
            try {
                if (!await ReadExactAsync(s, header.AsMemory(0, 2), ct).ConfigureAwait(false)) {
                    return new Frame(FrameKind.EOF, false, 0, default);
                }

                bool fin = (header[0] & 0x80) != 0;
                byte opcode = (byte)(header[0] & 0x0F);

                bool masked = (header[1] & 0x80) != 0;
                int len7 = (header[1] & 0x7F);

                ulong payloadLen;
                if (len7 < 126) {
                    payloadLen = (ulong)len7;
                }
                else if (len7 == 126) {
                    byte[] ext = _pool.Rent(2);
                    try {
                        if (!await ReadExactAsync(s, ext.AsMemory(0, 2), ct).ConfigureAwait(false)) {
                            return new Frame(FrameKind.EOF, false, 0, default);
                        }
                        payloadLen = (ulong)((ext[0] << 8) | ext[1]);
                    }
                    finally {
                        _pool.Return(ext);
                    }
                }
                else {
                    byte[] ext = _pool.Rent(8);
                    try {
                        if (!await ReadExactAsync(s, ext.AsMemory(0, 8), ct).ConfigureAwait(false)) {
                            return new Frame(FrameKind.EOF, false, 0, default);
                        }
                        payloadLen = ((ulong)ext[0] << 56)
                                     | ((ulong)ext[1] << 48)
                                     | ((ulong)ext[2] << 40)
                                     | ((ulong)ext[3] << 32)
                                     | ((ulong)ext[4] << 24)
                                     | ((ulong)ext[5] << 16)
                                     | ((ulong)ext[6] << 8)
                                     | ((ulong)ext[7]);
                    }
                    finally {
                        _pool.Return(ext);
                    }
                }

                if (payloadLen > (ulong)_maxMessageBytes) {
                    // we won't even read it
                    await CloseAsync(1009, "message too big").ConfigureAwait(false);
                    return new Frame(FrameKind.EOF, false, 0, default);
                }

                byte[] maskKey = Array.Empty<byte>();
                if (masked) {
                    maskKey = _pool.Rent(4);
                    if (!await ReadExactAsync(s, maskKey.AsMemory(0, 4), ct).ConfigureAwait(false)) {
                        _pool.Return(maskKey);
                        return new Frame(FrameKind.EOF, false, 0, default);
                    }
                }

                byte[] payloadBuf = payloadLen == 0 ? Array.Empty<byte>() : _pool.Rent((int)payloadLen);
                if (payloadLen > 0) {
                    if (!await ReadExactAsync(s, payloadBuf.AsMemory(0, (int)payloadLen), ct).ConfigureAwait(false)) {
                        if (masked) {
                            _pool.Return(maskKey);
                        }
                        _pool.Return(payloadBuf);
                        return new Frame(FrameKind.EOF, false, 0, default);
                    }
                }

                if (masked && payloadLen > 0) {
                    for (int i = 0; i < (int)payloadLen; i++) {
                        payloadBuf[i] ^= maskKey[i & 3];
                    }
                }

                if (masked) {
                    _pool.Return(maskKey);
                }

                ReadOnlyMemory<byte> mem = payloadLen == 0 ? ReadOnlyMemory<byte>.Empty : payloadBuf.AsMemory(0, (int)payloadLen);
                return new Frame(FrameKind.Data, fin, opcode, mem);
            }
            finally {
                _pool.Return(header);
            }
        }

        /// <summary>
        /// ReadExactAsync
        /// </summary>
        /// <param name="s"></param>
        /// <param name="buffer"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private static async ValueTask<bool> ReadExactAsync(Stream s, Memory<byte> buffer, CancellationToken ct) {
            int readTotal = 0;
            while (readTotal < buffer.Length) {
                int r = await s.ReadAsync(buffer.Slice(readTotal), ct).ConfigureAwait(false);
                if (r <= 0) {
                    return false;
                }
                readTotal += r;
            }
            return true;
        }

        #endregion frame read

    }

    /// <summary>
    /// Context passed to message handlers
    /// </summary>
    public sealed class WebSocketContext {

        /// <summary>
        /// The underlying HttpSession
        /// </summary>
        public HttpSession Session { get; }

        /// <summary>
        /// Hub attached to the module
        /// </summary>
        public WebSocketHub Hub { get; }

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
        internal WebSocketContext(HttpSession session, WebSocketHub hub, string requestPath) {
            Session = session;
            Hub = hub;
            RequestPath = requestPath;
        }

        /// <summary>
        /// Join a room and plug conn.HubBroadcast* for that room
        /// </summary>
        public ValueTask JoinRoomAsync(string room, WebSocketConnection conn) => Hub.JoinWithBroadcastAsync(room, conn);

        /// <summary>
        /// Leave a room
        /// </summary>
        public ValueTask LeaveRoomAsync(string room, WebSocketConnection conn) => Hub.LeaveAsync(room, conn);
    }

    /// <summary>
    /// Router (exact op match)
    /// </summary>
    public sealed class WebSocketRouter {

        /// <summary>
        /// Handlers dict
        /// </summary>
        private readonly Dictionary<string, WebSocketMessageHandler> _handlers = new(StringComparer.Ordinal);

        /// <summary>
        /// Handler for Unknown message
        /// </summary>
        private WebSocketMessageHandler? _unknown;

        /// <summary>
        /// Handler for binary
        /// </summary>
        private Func<WebSocketConnection, WebSocketContext, ReadOnlyMemory<byte>, ValueTask>? _binary;

        /// <summary>
        /// Map
        /// </summary>
        /// <param name="op"></param>
        /// <param name="handler"></param>
        /// <exception cref="ArgumentException"></exception>
        public void Map(string op, WebSocketMessageHandler handler) {
            if (string.IsNullOrWhiteSpace(op)) {
                throw new ArgumentException("op must not be empty.", nameof(op));
            }
            ArgumentNullException.ThrowIfNull(handler);
            _handlers[op] = handler;
        }

        /// <summary>
        /// OnUnknown
        /// </summary>
        /// <param name="handler"></param>
        public void OnUnknown(WebSocketMessageHandler handler) {
            ArgumentNullException.ThrowIfNull(handler);
            _unknown = handler;
        }

        /// <summary>
        /// OnBinary
        /// </summary>
        /// <param name="handler"></param>
        public void OnBinary(Func<WebSocketConnection, WebSocketContext, ReadOnlyMemory<byte>, ValueTask> handler) {
            ArgumentNullException.ThrowIfNull(handler);
            _binary = handler;
        }

        /// <summary>
        /// RunAsync
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="ctx"></param>
        /// <returns></returns>
        internal async ValueTask RunAsync(WebSocketConnection conn, WebSocketContext ctx) {
            await foreach (WebSocketMessage m in conn.ReadMessagesAsync()) {

                if (m.Kind == WebSocketMessageKind.Binary) {
                    if (_binary != null) {
                        await _binary(conn, ctx, m.Binary).ConfigureAwait(false);
                    }
                    continue;
                }

                string raw = m.Text ?? string.Empty;

                // try json
                try {
                    using JsonDocument doc = JsonDocument.Parse(raw);
                    JsonElement root = doc.RootElement;

                    string op = "";
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("op", out var opEl) &&
                        opEl.ValueKind == JsonValueKind.String) {
                        op = opEl.GetString() ?? "";
                    }

                    string? id = null;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out JsonElement idEl)) {
                        id = idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.ToString();
                    }

                    JsonElement? payload = null;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("payload", out JsonElement plEl)) {
                        payload = plEl;
                    }

                    WebSocketEnvelope env = new(op, id, true, raw, payload, root);

                    if (!string.IsNullOrEmpty(op) && _handlers.TryGetValue(op, out var h)) {
                        await h(conn, ctx, env).ConfigureAwait(false);
                    }
                    else if (_unknown != null) {
                        await _unknown(conn, ctx, env).ConfigureAwait(false);
                    }
                }
                catch {
                    // not json
                    if (_unknown != null) {
                        WebSocketEnvelope env = new("", null, false, raw, null, null);
                        await _unknown(conn, ctx, env).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// WebSocketHub
    /// </summary>
    public sealed class WebSocketHub {

        /// <summary>
        /// Rooms in the current WebSocket Module
        /// </summary>
        private readonly ConcurrentDictionary<string, HubRoom> _rooms = new(StringComparer.Ordinal);

        /// <summary>
        /// Join a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="conn"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public ValueTask JoinAsync(string room, WebSocketConnection conn) {
            if (string.IsNullOrWhiteSpace(room)) {
                throw new ArgumentException("room must not be empty.", nameof(room));
            }
            ArgumentNullException.ThrowIfNull(conn);

            HubRoom? r = _rooms.GetOrAdd(room, static _ => new HubRoom());
            r?.Join(conn);

            // auto-cleanup on close
            conn.Closed += () => {
                try {
                    r?.Leave(conn);
                }
                catch { }
                // remove empty room
                if (r?.Count == 0) {
                    _rooms.TryRemove(room, out _);
                }
            };

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Leave a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="conn"></param>
        /// <returns></returns>
        public ValueTask LeaveAsync(string room, WebSocketConnection conn) {
            if (_rooms.TryGetValue(room, out HubRoom? r) && r != null) {
                r.Leave(conn);
                if (r.Count == 0) {
                    _rooms.TryRemove(room, out _);
                }
            }
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Broadcast Text to all Clients of a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="text"></param>
        /// <param name="except"></param>
        /// <returns></returns>
        public async ValueTask BroadcastTextAsync(string room, string text, WebSocketConnection? except = null) {
            if (!_rooms.TryGetValue(room, out var r)) {
                return;
            }
            await r.BroadcastTextAsync(text, except).ConfigureAwait(false);
        }

        /// <summary>
        /// Broadcast Binary to all Clients of a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="data"></param>
        /// <param name="except"></param>
        /// <returns></returns>
        public async ValueTask BroadcastBinaryAsync(string room, ReadOnlyMemory<byte> data, WebSocketConnection? except = null) {
            if (!_rooms.TryGetValue(room, out var r)) {
                return;
            }
            await r.BroadcastBinaryAsync(data, except).ConfigureAwait(false);
        }

        /// <summary>
        /// HubRoom
        /// </summary>
        private sealed class HubRoom {

            /// <summary>
            /// Clients in the current Room
            /// </summary>
            private readonly ConcurrentDictionary<Guid, WebSocketConnection> _conns = new();

            public int Count => _conns.Count;

            /// <summary>
            /// Join a Room
            /// </summary>
            /// <param name="c"></param>
            public void Join(WebSocketConnection c) => _conns[c.Id] = c;

            /// <summary>
            /// Leave a Room
            /// </summary>
            /// <param name="c"></param>
            public void Leave(WebSocketConnection c) => _conns.TryRemove(c.Id, out _);

            /// <summary>
            /// Broadcast Text to all Clients of the current Room
            /// </summary>
            /// <param name="text"></param>
            /// <param name="except"></param>
            /// <returns></returns>
            public async ValueTask BroadcastTextAsync(string text, WebSocketConnection? except) {
                List<Exception>? errors = null;

                foreach (KeyValuePair<Guid, WebSocketConnection> kv in _conns) {
                    WebSocketConnection c = kv.Value;
                    if (except != null && ReferenceEquals(c, except)) {
                        continue;
                    }

                    try {
                        await c.SendTextAsync(text).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        (errors ??= new()).Add(ex);
                    }
                }

                _ = errors;
            }

            /// <summary>
            /// Broadcast Binary to all Clients of the current Room
            /// </summary>
            /// <param name="data"></param>
            /// <param name="except"></param>
            /// <returns></returns>
            public async ValueTask BroadcastBinaryAsync(ReadOnlyMemory<byte> data, WebSocketConnection? except) {
                List<Exception>? errors = null;

                foreach (KeyValuePair<Guid, WebSocketConnection> kv in _conns) {
                    WebSocketConnection c = kv.Value;
                    if (except != null && ReferenceEquals(c, except)) {
                        continue;
                    }

                    try {
                        await c.SendBinaryAsync(data).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        (errors ??= new()).Add(ex);
                    }
                }

                _ = errors;
            }
        }
    }

    /// <summary>
    /// Make hub plug broadcasters into connection for a room
    /// </summary>
    internal static class WebSocketHubEndpointExtensions {

        /// <summary>
        /// JoinWithBroadcastAsync
        /// </summary>
        /// <param name="hub"></param>
        /// <param name="room"></param>
        /// <param name="conn"></param>
        /// <returns></returns>
        public static async ValueTask JoinWithBroadcastAsync(this WebSocketHub hub, string room, WebSocketConnection conn) {
            await hub.JoinAsync(room, conn).ConfigureAwait(false);

            conn.SetHubBroadcasters(
                broadcastText: (text, except) => hub.BroadcastTextAsync(room, text, except),
                broadcastBinary: (data, except) => hub.BroadcastBinaryAsync(room, data, except)
            );
        }
    }

    /// <summary>
    /// WebSocketMessageHandler
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="ctx"></param>
    /// <param name="msg"></param>
    /// <returns></returns>
    public delegate ValueTask WebSocketMessageHandler(WebSocketConnection conn, WebSocketContext ctx, WebSocketEnvelope msg);

    /// <summary>
    /// Envelope used for routing
    ///
    /// JSON input example:
    /// { "op": "chat/join", "id": "42", "payload": { ... } }
    ///
    /// If the incoming message isn't JSON, IsJson=false and Op="".
    /// </summary>
    /// <param name="Op"></param>
    /// <param name="Id"></param>
    /// <param name="IsJson"></param>
    /// <param name="RawText"></param>
    /// <param name="Payload"></param>
    /// <param name="Root"></param>
    public readonly record struct WebSocketEnvelope(string Op, string? Id, bool IsJson, string RawText, JsonElement? Payload, JsonElement? Root) {

        /// <summary>
        /// Deserialize the whole payload into T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public bool TryGetPayload<T>(out T? obj) {
            obj = default;

            if (Payload == null) {
                return false;
            }
            if (Payload.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
                return false;
            }

            try {
                obj = Payload.Value.Deserialize<T>();
                return obj != null || default(T) == null; // true for value types too
            }
            catch {
                obj = default;
                return false;
            }
        }

        /// <summary>
        /// Deserialize a property of payload into T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="obj"></param>
        /// <returns></returns>
        public bool TryGetPayload<T>(string name, out T? obj) {
            obj = default;

            if (Payload == null || Payload.Value.ValueKind != JsonValueKind.Object || !Payload.Value.TryGetProperty(name, out var el)) {
                return false;
            }
            if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) {
                return false;
            }

            try {
                obj = el.Deserialize<T>();
                return obj != null || default(T) == null;
            }
            catch {
                obj = default;
                return false;
            }
        }

    }

    /// <summary>
    /// WebSocketMessage
    /// </summary>
    /// <param name="Kind"></param>
    /// <param name="Text"></param>
    /// <param name="Binary"></param>
    public readonly record struct WebSocketMessage(WebSocketMessageKind Kind, string? Text, ReadOnlyMemory<byte> Binary);

    /// <summary>
    /// WebSocket Message Kind
    /// </summary>
    public enum WebSocketMessageKind : byte {
        /// <summary>
        /// Text
        /// </summary>
        Text = 1,
        /// <summary>
        /// Binary
        /// </summary>
        Binary = 2
    }

}
