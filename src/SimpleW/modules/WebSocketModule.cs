using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimpleW.Observability;


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
        /// Optional authorization callback. Return false to reject the WebSocket handshake.
        /// </summary>
        public Func<HttpSession, bool>? Authorize { get; set; }

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
            if (string.IsNullOrWhiteSpace(RequiredSubProtocol)) {
                RequiredSubProtocol = null;
            }
            else {
                RequiredSubProtocol = RequiredSubProtocol.Trim();
                if (!IsValidProtocolToken(RequiredSubProtocol)) {
                    throw new ArgumentException("WebSocketOptions.RequiredSubProtocol must be a valid HTTP token.", nameof(RequiredSubProtocol));
                }
            }
            if (string.IsNullOrWhiteSpace(AutoJoinRoom)) {
                AutoJoinRoom = null;
            }
            return this;
        }

        /// <summary>
        /// IsValidProtocolToken
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool IsValidProtocolToken(string value) {
            foreach (char c in value) {
                if (c <= 0x20 || c >= 0x7F) {
                    return false;
                }

                if (c is '(' or ')' or '<' or '>' or '@' or ',' or ';' or ':' or '\\' or '"' or '/' or '[' or ']' or '?' or '=' or '{' or '}') {
                    return false;
                }
            }

            return value.Length > 0;
        }

        internal string PrefixWildCard => Prefix + "/*";

    }

    /// <summary>
    /// Module: single endpoint + message routing
    /// </summary>
    internal sealed class WebSocketModule : IHttpModule {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<WebSocketModule>();

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
            // accept prefix
            server.MapGet(_options.Prefix, (HttpSession session) => HandlerAsync(session));
            // accept prefix wildcard as fallback (but will enforce exact match)
            server.MapGet(_options.PrefixWildCard, (HttpSession session) => HandlerAsync(session));

            _log.Info($"installed with prefix {_options.Prefix}");
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

            if (_options.Authorize != null && !_options.Authorize(session)) {
                await session.Response.Status(403).Text("Forbidden").SendAsync().ConfigureAwait(false);
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
                if (!ContainsHeaderToken(wsProtocols, _options.RequiredSubProtocol!, StringComparison.Ordinal)) {
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
            await FlushDeferredAsync(session.Transport.Output).ConfigureAwait(false);

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
        /// ISimpleWTransportDeferredFlushFeature
        /// </summary>
        /// <param name="output"></param>
        /// <returns></returns>
        private static ValueTask FlushDeferredAsync(ISimpleWTransportOutput output) {
            if (output is ISimpleWTransportDeferredFlushFeature deferredFlush) {
                return deferredFlush.FlushDeferredAsync();
            }

            return ValueTask.CompletedTask;
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
                || !ContainsHeaderToken(upgrade, "websocket")
            ) {
                return false;
            }
            if (!req.Headers.TryGetValue("Connection", out var conn)
                || !ContainsHeaderToken(conn, "Upgrade")
            ) {
                return false;
            }

            if (!req.Headers.TryGetValue("Sec-WebSocket-Key", out wsKey) || string.IsNullOrWhiteSpace(wsKey)) {
                return false;
            }
            wsKey = wsKey.Trim();
            if (!IsValidWebSocketKey(wsKey)) {
                return false;
            }

            req.Headers.TryGetValue("Sec-WebSocket-Version", out wsVersion);
            wsVersion = wsVersion?.Trim();
            req.Headers.TryGetValue("Sec-WebSocket-Protocol", out wsProtocols);
            return true;
        }

        /// <summary>
        /// ContainsHeaderToken
        /// </summary>
        /// <param name="header"></param>
        /// <param name="token"></param>
        /// <param name="comparison"></param>
        /// <returns></returns>
        private static bool ContainsHeaderToken(string? header, string token, StringComparison comparison = StringComparison.OrdinalIgnoreCase) {
            if (string.IsNullOrWhiteSpace(header)) {
                return false;
            }
            foreach (string item in header.Split(',')) {
                if (string.Equals(item.Trim(), token, comparison)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// IsValidWebSocketKey
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static bool IsValidWebSocketKey(string key) {
            Span<byte> decoded = stackalloc byte[16];
            return Convert.TryFromBase64String(key, decoded, out int written) && written == 16;
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

        #region opcodes

        private const byte OpcodeContinuation = 0x0;
        private const byte OpcodeText = 0x1;
        private const byte OpcodeBinary = 0x2;
        private const byte OpcodeClose = 0x8;
        private const byte OpcodePing = 0x9;
        private const byte OpcodePong = 0xA;

        private const ushort CloseStatusProtocolError = 1002;
        private const ushort CloseStatusInvalidPayloadData = 1007;
        private const ushort CloseStatusMessageTooBig = 1009;

        private const int MaxControlPayloadBytes = 125;
        private const int MaxCloseReasonBytes = 123;
        private const int InitialReassemblyBytes = 256;
        private const int MaxCoalescedSendBytes = 64 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        #endregion opcodes

        /// <summary>
        /// Serializes all outbound frame writes for this connection.
        /// This prevents concurrent sends from interleaving bytes on the underlying transport.
        /// </summary>
        private readonly SemaphoreSlim _sendLock = new(1, 1);


        /// <summary>
        /// Underlying detached transport.
        /// </summary>
        private readonly ISimpleWEngine _transport;
        private readonly ISimpleWTransportDeferredFlushFeature? _deferredFlush;
        private readonly int _maxMessageBytes;
        private readonly TimeSpan? _pingInterval;

        private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
        private int _closeStarted;
        private int _closedEventFired;

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
        public EndPoint? RemoteEndPoint => _transport.RemoteEndPoint;

        internal bool IsClosed => Volatile.Read(ref _closedEventFired) != 0;

        /// <summary>
        /// Constructor
        /// </summary>
        public WebSocketConnection(HttpSession session, int maxMessageBytes, TimeSpan? pingInterval) {
            _transport = session.Transport;
            _deferredFlush = _transport.Output as ISimpleWTransportDeferredFlushFeature;
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
        public async ValueTask SendTextAsync(string text) {
            if (string.IsNullOrEmpty(text)) {
                await SendFrameAsync(opcode: OpcodeText, ReadOnlyMemory<byte>.Empty).ConfigureAwait(false);
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(text);
            byte[] rented = _pool.Rent(byteCount);

            try {
                int written = Encoding.UTF8.GetBytes(text.AsSpan(), rented.AsSpan());
                await SendFrameAsync(opcode: OpcodeText, rented.AsMemory(0, written)).ConfigureAwait(false);
            }
            finally {
                _pool.Return(rented);
            }
        }

        /// <summary>
        /// SendBinaryAsync
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public ValueTask SendBinaryAsync(ReadOnlyMemory<byte> payload) => SendFrameAsync(opcode: OpcodeBinary, payload);

        /// <summary>
        /// PingAsync
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        public ValueTask PingAsync(ReadOnlyMemory<byte> payload = default) => SendFrameAsync(opcode: OpcodePing, payload);

        /// <summary>
        /// CloseAsync
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        /// <returns></returns>
        public async ValueTask CloseAsync(ushort code = 1000, string? reason = null) {
            if (Volatile.Read(ref _closeStarted) != 0) {
                return;
            }
            if (!IsValidCloseStatusCode(code)) {
                throw new ArgumentOutOfRangeException(nameof(code), "Invalid WebSocket close status code.");
            }

            byte[] reasonBytes = EncodeCloseReason(reason);
            int len = 2 + reasonBytes.Length;
            byte[] buf = _pool.Rent(len);

            try {
                buf[0] = (byte)(code >> 8);
                buf[1] = (byte)(code & 0xFF);
                if (reasonBytes.Length > 0) {
                    Buffer.BlockCopy(reasonBytes, 0, buf, 2, reasonBytes.Length);
                }

                await SendCloseFrameAsync(buf.AsMemory(0, len)).ConfigureAwait(false);
            }
            finally {
                _pool.Return(buf);
            }
        }

        /// <summary>
        /// SendCloseFrameAsync
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private async ValueTask SendCloseFrameAsync(ReadOnlyMemory<byte> payload) {
            if (payload.Length > MaxControlPayloadBytes) {
                throw new ArgumentOutOfRangeException(nameof(payload), "Close frame payload must be 125 bytes or less.");
            }
            if (Interlocked.Exchange(ref _closeStarted, 1) != 0) {
                return;
            }

            try {
                await SendFrameCoreAsync(OpcodeClose, payload).ConfigureAwait(false);
            }
            finally {
                MarkClosed();
            }
        }

        /// <summary>
        /// MarkClosed
        /// </summary>
        private void MarkClosed() {
            Interlocked.Exchange(ref _closeStarted, 1);
            if (Interlocked.Exchange(ref _closedEventFired, 1) != 0) {
                return;
            }

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
            PooledMessageBuffer? reassembly = null;
            byte reassemblyOpcode = 0;

            using CancellationTokenSource pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task? pingTask = null;
            if (_pingInterval.HasValue) {
                pingTask = RunPingLoopAsync(_pingInterval.Value, pingCts.Token);
            }

            try {
                while (!ct.IsCancellationRequested) {
                    Frame frame = default;
                    WebSocketProtocolException? readError = null;
                    try {
                        frame = await ReadFrameAsync(ct).ConfigureAwait(false);
                    }
                    catch (WebSocketProtocolException ex) {
                        readError = ex;
                    }

                    if (readError != null) {
                        await CloseAsync(readError.CloseStatusCode, readError.Message).ConfigureAwait(false);
                        yield break;
                    }
                    if (frame.Kind == FrameKind.EOF) {
                        yield break;
                    }

                    byte[]? completeMessage = null;
                    byte completeOpcode = 0;
                    WebSocketProtocolException? processError = null;
                    bool stop = false;

                    try {
                        try {
                            if (frame.Opcode == OpcodePing) {
                                await SendFrameAsync(OpcodePong, frame.Payload).ConfigureAwait(false);
                                continue;
                            }

                            if (frame.Opcode == OpcodePong) {
                                continue;
                            }

                            if (frame.Opcode == OpcodeClose) {
                                await SendCloseFrameAsync(frame.Payload).ConfigureAwait(false);
                                stop = true;
                            }
                            else if (frame.Opcode == OpcodeContinuation) {
                                if (reassembly == null) {
                                    throw new WebSocketProtocolException(CloseStatusProtocolError, "unexpected continuation frame");
                                }

                                reassembly.Append(frame.Payload.Span);
                                if (frame.Fin) {
                                    completeMessage = reassembly.ToArray();
                                    completeOpcode = reassemblyOpcode;
                                    reassembly.Dispose();
                                    reassembly = null;
                                    reassemblyOpcode = 0;
                                }
                            }
                            else {
                                if (reassembly != null) {
                                    throw new WebSocketProtocolException(CloseStatusProtocolError, "fragmented message interrupted");
                                }

                                if (frame.Fin) {
                                    completeMessage = CopyPayload(frame.Payload);
                                    completeOpcode = frame.Opcode;
                                }
                                else {
                                    reassembly = new PooledMessageBuffer(_pool, _maxMessageBytes, frame.Payload.Length);
                                    reassemblyOpcode = frame.Opcode;
                                    reassembly.Append(frame.Payload.Span);
                                }
                            }
                        }
                        catch (WebSocketProtocolException ex) {
                            processError = ex;
                        }
                    }
                    finally {
                        frame.Dispose();
                    }

                    if (processError != null) {
                        await CloseAsync(processError.CloseStatusCode, processError.Message).ConfigureAwait(false);
                        yield break;
                    }
                    if (stop) {
                        yield break;
                    }
                    if (completeMessage == null) {
                        continue;
                    }

                    if (completeOpcode == OpcodeText && !IsValidUtf8(completeMessage)) {
                        await CloseAsync(CloseStatusInvalidPayloadData, "invalid utf-8").ConfigureAwait(false);
                        yield break;
                    }

                    if (completeOpcode == OpcodeText) {
                        yield return new WebSocketMessage(WebSocketMessageKind.Text, completeMessage, default);
                    }
                    else {
                        yield return new WebSocketMessage(WebSocketMessageKind.Binary, default, completeMessage);
                    }
                }
            }
            finally {
                reassembly?.Dispose();

                try {
                    pingCts.Cancel();
                }
                catch { }

                if (pingTask != null) {
                    try { await pingTask.ConfigureAwait(false); }
                    catch { }
                }

                MarkClosed();
            }
        }

        /// <summary>
        /// CopyPayload
        /// </summary>
        /// <param name="payload"></param>
        /// <returns></returns>
        private static byte[] CopyPayload(ReadOnlyMemory<byte> payload) {
            byte[] message = GC.AllocateUninitializedArray<byte>(payload.Length);
            if (payload.Length > 0) {
                payload.Span.CopyTo(message);
            }
            return message;
        }

        /// <summary>
        /// IsValidUtf8
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool IsValidUtf8(ReadOnlySpan<byte> value) {
            try {
                StrictUtf8.GetCharCount(value);
                return true;
            }
            catch (DecoderFallbackException) {
                return false;
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

        /// <summary>
        /// PooledMessageBuffer
        /// </summary>
        private sealed class PooledMessageBuffer : IDisposable {

            private readonly ArrayPool<byte> _pool;
            private readonly int _maxLength;
            private byte[] _buffer;

            public int Length { get; private set; }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="pool"></param>
            /// <param name="maxLength"></param>
            /// <param name="firstChunkLength"></param>
            public PooledMessageBuffer(ArrayPool<byte> pool, int maxLength, int firstChunkLength) {
                _pool = pool;
                _maxLength = maxLength;

                int initialCapacity = Math.Min(maxLength, Math.Max(InitialReassemblyBytes, firstChunkLength));
                _buffer = _pool.Rent(initialCapacity);
            }


            public void Append(ReadOnlySpan<byte> data) {
                if (data.Length > _maxLength - Length) {
                    throw new WebSocketProtocolException(CloseStatusMessageTooBig, "message too big");
                }

                EnsureCapacity(Length + data.Length);
                data.CopyTo(_buffer.AsSpan(Length));
                Length += data.Length;
            }

            public byte[] ToArray() {
                byte[] result = GC.AllocateUninitializedArray<byte>(Length);
                if (Length > 0) {
                    Buffer.BlockCopy(_buffer, 0, result, 0, Length);
                }
                return result;
            }

            private void EnsureCapacity(int required) {
                if (required <= _buffer.Length) {
                    return;
                }

                int newLength = Math.Max(required, _buffer.Length * 2);
                if (newLength > _maxLength) {
                    newLength = _maxLength;
                }

                byte[] next = _pool.Rent(newLength);
                if (Length > 0) {
                    Buffer.BlockCopy(_buffer, 0, next, 0, Length);
                }

                _pool.Return(_buffer);
                _buffer = next;
            }

            public void Dispose() {
                byte[] buffer = _buffer;
                if (buffer.Length == 0) {
                    return;
                }

                _buffer = Array.Empty<byte>();
                Length = 0;
                _pool.Return(buffer);
            }
        }

        #endregion read messages

        #region frame write

        /// <summary>
        /// Frame
        /// </summary>
        private readonly struct Frame : IDisposable {

            public readonly FrameKind Kind;
            public readonly bool Fin;
            public readonly byte Opcode;
            public readonly ReadOnlyMemory<byte> Payload;

            private readonly byte[]? _pooledBuffer;
            private readonly ArrayPool<byte>? _pool;

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
                _pooledBuffer = null;
                _pool = null;
            }

            /// <summary>
            /// Constructor with pooled ownership
            /// </summary>
            /// <param name="kind"></param>
            /// <param name="fin"></param>
            /// <param name="opcode"></param>
            /// <param name="pooledBuffer"></param>
            /// <param name="payloadLength"></param>
            /// <param name="pool"></param>
            public Frame(FrameKind kind, bool fin, byte opcode, byte[] pooledBuffer, int payloadLength, ArrayPool<byte> pool) {
                Kind = kind;
                Fin = fin;
                Opcode = opcode;
                Payload = pooledBuffer.AsMemory(0, payloadLength);
                _pooledBuffer = pooledBuffer;
                _pool = pool;
            }

            /// <summary>
            /// Dispose pooled payload if owned
            /// </summary>
            public void Dispose() {
                if (_pooledBuffer != null && _pool != null) {
                    _pool.Return(_pooledBuffer);
                }
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
        /// WebSocketProtocolException
        /// </summary>
        private sealed class WebSocketProtocolException : Exception {

            public ushort CloseStatusCode { get; }

            public WebSocketProtocolException(ushort closeStatusCode, string message) : base(message) {
                CloseStatusCode = closeStatusCode;
            }

        }

        /// <summary>
        /// SendFrameAsync
        /// </summary>
        /// <param name="opcode"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        private ValueTask SendFrameAsync(byte opcode, ReadOnlyMemory<byte> payload) {
            ValidateOutgoingFrame(opcode, payload.Length);
            return SendFrameCoreAsync(opcode, payload);
        }

        /// <summary>
        /// SendFrameCoreAsync
        /// </summary>
        /// <param name="opcode"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        private async ValueTask SendFrameCoreAsync(byte opcode, ReadOnlyMemory<byte> payload) {
            if (Volatile.Read(ref _closeStarted) != 0 && opcode != OpcodeClose) {
                return;
            }

            bool writeFailed = false;
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try {
                try {
                    if (Volatile.Read(ref _closeStarted) != 0 && opcode != OpcodeClose) {
                        return;
                    }

                    int payloadLen = payload.Length;

                    int headerLen = payloadLen switch {
                        <= 125 => 2,
                        <= ushort.MaxValue => 4,
                        _ => 10
                    };

                    byte[] header = _pool.Rent(headerLen);
                    try {
                        int writtenHeader = WriteFrameHeader(header, opcode, payloadLen);

                        await WriteFrameBytesAsync(header, writtenHeader, payload).ConfigureAwait(false);
                        await FlushDeferredAsync().ConfigureAwait(false);
                    }
                    finally {
                        _pool.Return(header);
                    }
                }
                catch {
                    writeFailed = true;
                    throw;
                }
            }
            finally {
                _sendLock.Release();
                if (writeFailed) {
                    MarkClosed();
                }
            }
        }

        /// <summary>
        /// Flush deferred transport writes, when supported.
        /// </summary>
        private ValueTask FlushDeferredAsync() {
            if (_deferredFlush == null) {
                return ValueTask.CompletedTask;
            }

            return _deferredFlush.FlushDeferredAsync();
        }

        /// <summary>
        /// WriteFrameBytesAsync
        /// </summary>
        /// <param name="header"></param>
        /// <param name="headerLength"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        /// <exception cref="IOException"></exception>
        private async ValueTask WriteFrameBytesAsync(byte[] header, int headerLength, ReadOnlyMemory<byte> payload) {
            int totalLength = headerLength + payload.Length;

            if (payload.IsEmpty) {
                long written = await _transport.Output.WriteAsync(header.AsMemory(0, headerLength)).ConfigureAwait(false);
                if (written != headerLength) {
                    throw new IOException("Incomplete WebSocket frame write.");
                }
                return;
            }

            if (totalLength <= MaxCoalescedSendBytes) {
                byte[] frame = _pool.Rent(totalLength);
                try {
                    Buffer.BlockCopy(header, 0, frame, 0, headerLength);
                    payload.Span.CopyTo(frame.AsSpan(headerLength, payload.Length));

                    long written = await _transport.Output.WriteAsync(frame.AsMemory(0, totalLength)).ConfigureAwait(false);
                    if (written != totalLength) {
                        throw new IOException("Incomplete WebSocket frame write.");
                    }
                }
                finally {
                    _pool.Return(frame);
                }
                return;
            }

            if (MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> payloadSegment) && payloadSegment.Array != null) {
                long written = await _transport.Output.WriteAsync(
                    new ArraySegment<byte>(header, 0, headerLength),
                    payloadSegment
                ).ConfigureAwait(false);
                if (written != totalLength) {
                    throw new IOException("Incomplete WebSocket frame write.");
                }
                return;
            }

            byte[] fallback = _pool.Rent(totalLength);
            try {
                Buffer.BlockCopy(header, 0, fallback, 0, headerLength);
                payload.Span.CopyTo(fallback.AsSpan(headerLength, payload.Length));

                long written = await _transport.Output.WriteAsync(fallback.AsMemory(0, totalLength)).ConfigureAwait(false);
                if (written != totalLength) {
                    throw new IOException("Incomplete WebSocket frame write.");
                }
            }
            finally {
                _pool.Return(fallback);
            }
        }

        /// <summary>
        /// ValidateOutgoingFrame
        /// </summary>
        /// <param name="opcode"></param>
        /// <param name="payloadLen"></param>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void ValidateOutgoingFrame(byte opcode, int payloadLen) {
            if (!IsKnownOpcode(opcode) || opcode == OpcodeContinuation) {
                throw new InvalidOperationException("Invalid WebSocket opcode for outbound frame.");
            }

            if (IsControlOpcode(opcode) && payloadLen > MaxControlPayloadBytes) {
                throw new ArgumentOutOfRangeException(nameof(payloadLen), "Control frame payload must be 125 bytes or less.");
            }
        }

        /// <summary>
        /// Write WebSocket frame header into buffer and return header length.
        /// Server frames are never masked.
        /// </summary>
        private static int WriteFrameHeader(byte[] header, byte opcode, int payloadLen) {
            header[0] = (byte)(0x80 | (opcode & 0x0F)); // FIN=1

            if (payloadLen <= 125) {
                header[1] = (byte)payloadLen;
                return 2;
            }

            if (payloadLen <= ushort.MaxValue) {
                header[1] = 126;
                header[2] = (byte)((payloadLen >> 8) & 0xFF);
                header[3] = (byte)(payloadLen & 0xFF);
                return 4;
            }

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

            return 10;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="opcode"></param>
        /// <returns></returns>
        private static bool IsKnownOpcode(byte opcode) {
            return opcode is OpcodeContinuation or OpcodeText or OpcodeBinary or OpcodeClose or OpcodePing or OpcodePong;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="opcode"></param>
        /// <returns></returns>
        private static bool IsControlOpcode(byte opcode) {
            return opcode >= 0x8;
        }

        /// <summary>
        /// EncodeCloseReason
        /// </summary>
        /// <param name="reason"></param>
        /// <returns></returns>
        private static byte[] EncodeCloseReason(string? reason) {
            if (string.IsNullOrEmpty(reason)) {
                return Array.Empty<byte>();
            }

            int bytes = 0;
            int chars = 0;
            foreach (Rune rune in reason.EnumerateRunes()) {
                int runeBytes = rune.Utf8SequenceLength;
                if (bytes + runeBytes > MaxCloseReasonBytes) {
                    break;
                }

                bytes += runeBytes;
                chars += rune.Utf16SequenceLength;
            }

            return chars == 0 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(chars == reason.Length ? reason : reason.Substring(0, chars));
        }

        /// <summary>
        /// IsValidCloseStatusCode
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        private static bool IsValidCloseStatusCode(ushort code) {
            if (code is >= 1000 and <= 1014) {
                return code is not (1004 or 1005 or 1006);
            }

            return code is >= 3000 and <= 4999;
        }

        #endregion frame write

        #region frame read

        /// <summary>
        /// ReadFrameAsync
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask<Frame> ReadFrameAsync(CancellationToken ct) {
            byte[] scratch = _pool.Rent(14);
            try {
                if (!await ReadExactAsync(_transport, scratch.AsMemory(0, 2), ct).ConfigureAwait(false)) {
                    return new Frame(FrameKind.EOF, false, 0, default);
                }

                byte first = scratch[0];
                byte second = scratch[1];
                bool fin = (first & 0x80) != 0;
                bool reserved = (first & 0x70) != 0;
                byte opcode = (byte)(first & 0x0F);
                bool masked = (second & 0x80) != 0;
                int len7 = second & 0x7F;

                if (reserved) {
                    throw new WebSocketProtocolException(CloseStatusProtocolError, "reserved bits set");
                }
                if (!IsKnownOpcode(opcode)) {
                    throw new WebSocketProtocolException(CloseStatusProtocolError, "unknown opcode");
                }
                if (!masked) {
                    throw new WebSocketProtocolException(CloseStatusProtocolError, "client frames must be masked");
                }

                bool control = IsControlOpcode(opcode);
                if (control && !fin) {
                    throw new WebSocketProtocolException(CloseStatusProtocolError, "fragmented control frame");
                }

                ulong payloadLen;
                if (len7 < 126) {
                    payloadLen = (ulong)len7;
                }
                else if (len7 == 126) {
                    if (!await ReadExactAsync(_transport, scratch.AsMemory(2, 2), ct).ConfigureAwait(false)) {
                        return new Frame(FrameKind.EOF, false, 0, default);
                    }

                    payloadLen = (ulong)((scratch[2] << 8) | scratch[3]);
                    if (payloadLen < 126) {
                        throw new WebSocketProtocolException(CloseStatusProtocolError, "non-minimal payload length");
                    }
                }
                else {
                    if (!await ReadExactAsync(_transport, scratch.AsMemory(2, 8), ct).ConfigureAwait(false)) {
                        return new Frame(FrameKind.EOF, false, 0, default);
                    }

                    if ((scratch[2] & 0x80) != 0) {
                        throw new WebSocketProtocolException(CloseStatusProtocolError, "invalid 64-bit payload length");
                    }

                    payloadLen = ((ulong)scratch[2] << 56)
                                 | ((ulong)scratch[3] << 48)
                                 | ((ulong)scratch[4] << 40)
                                 | ((ulong)scratch[5] << 32)
                                 | ((ulong)scratch[6] << 24)
                                 | ((ulong)scratch[7] << 16)
                                 | ((ulong)scratch[8] << 8)
                                 | scratch[9];
                    if (payloadLen <= ushort.MaxValue) {
                        throw new WebSocketProtocolException(CloseStatusProtocolError, "non-minimal payload length");
                    }
                }

                if (control && payloadLen > MaxControlPayloadBytes) {
                    throw new WebSocketProtocolException(CloseStatusProtocolError, "control frame too large");
                }
                if (!control && payloadLen > (ulong)_maxMessageBytes) {
                    throw new WebSocketProtocolException(CloseStatusMessageTooBig, "message too big");
                }

                if (!await ReadExactAsync(_transport, scratch.AsMemory(10, 4), ct).ConfigureAwait(false)) {
                    return new Frame(FrameKind.EOF, false, 0, default);
                }

                if (payloadLen == 0) {
                    if (opcode == OpcodeClose) {
                        ValidateCloseFramePayload(ReadOnlySpan<byte>.Empty);
                    }
                    return new Frame(FrameKind.Data, fin, opcode, ReadOnlyMemory<byte>.Empty);
                }

                int length = checked((int)payloadLen);
                byte[] payloadBuf = _pool.Rent(length);
                try {
                    if (!await ReadExactAsync(_transport, payloadBuf.AsMemory(0, length), ct).ConfigureAwait(false)) {
                        _pool.Return(payloadBuf);
                        return new Frame(FrameKind.EOF, false, 0, default);
                    }

                    ApplyMask(payloadBuf.AsSpan(0, length), scratch.AsSpan(10, 4));
                    if (opcode == OpcodeClose) {
                        ValidateCloseFramePayload(payloadBuf.AsSpan(0, length));
                    }

                    return new Frame(FrameKind.Data, fin, opcode, payloadBuf, length, _pool);
                }
                catch {
                    _pool.Return(payloadBuf);
                    throw;
                }
            }
            finally {
                _pool.Return(scratch);
            }
        }

        private static void ApplyMask(Span<byte> payload, ReadOnlySpan<byte> maskKey) {
            for (int i = 0; i < payload.Length; i++) {
                payload[i] ^= maskKey[i & 3];
            }
        }

        private static void ValidateCloseFramePayload(ReadOnlySpan<byte> payload) {
            if (payload.Length == 1) {
                throw new WebSocketProtocolException(CloseStatusProtocolError, "invalid close payload");
            }
            if (payload.Length < 2) {
                return;
            }

            ushort code = (ushort)((payload[0] << 8) | payload[1]);
            if (!IsValidCloseStatusCode(code)) {
                throw new WebSocketProtocolException(CloseStatusProtocolError, "invalid close status code");
            }
            if (payload.Length > 2 && !IsValidUtf8(payload.Slice(2))) {
                throw new WebSocketProtocolException(CloseStatusInvalidPayloadData, "invalid close reason");
            }
        }

        /// <summary>
        /// ReadExactAsync
        /// </summary>
        /// <param name="transport"></param>
        /// <param name="buffer"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private static async ValueTask<bool> ReadExactAsync(ISimpleWEngine transport, Memory<byte> buffer, CancellationToken ct) {
            ISimpleWTransportInput input = transport.Input;
            int readTotal = 0;
            while (readTotal < buffer.Length) {
                SimpleWTransportReadResult result = await input.ReadAsync(ct).ConfigureAwait(false);
                ReadOnlySequence<byte> source = result.Buffer;
                if (source.Length == 0) {
                    if (result.IsCompleted) {
                        return false;
                    }
                    continue;
                }

                int toCopy = checked((int)Math.Min(buffer.Length - readTotal, source.Length));
                source.Slice(0, toCopy).CopyTo(buffer.Slice(readTotal, toCopy).Span);
                input.AdvanceTo(toCopy, toCopy);
                readTotal += toCopy;
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

                ReadOnlyMemory<byte> rawUtf8 = m.Utf8Text;

                if (!LooksLikeJsonObject(rawUtf8)) {
                    if (_unknown != null) {
                        WebSocketEnvelope env = new("", null, false, rawUtf8, default);
                        await _unknown(conn, ctx, env).ConfigureAwait(false);
                    }
                    continue;
                }

                if (!TryParseEnvelope(rawUtf8, out WebSocketEnvelope env2)) {
                    if (_unknown != null) {
                        WebSocketEnvelope env = new("", null, false, rawUtf8, default);
                        await _unknown(conn, ctx, env).ConfigureAwait(false);
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(env2.Op) && _handlers.TryGetValue(env2.Op, out WebSocketMessageHandler? h)) {
                    await h(conn, ctx, env2).ConfigureAwait(false);
                }
                else if (_unknown != null) {
                    await _unknown(conn, ctx, env2).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// True if first significant UTF-8 byte is '{'
        /// </summary>
        /// <param name="utf8"></param>
        /// <returns></returns>
        private static bool LooksLikeJsonObject(ReadOnlyMemory<byte> utf8) {
            ReadOnlySpan<byte> span = utf8.Span;

            int i = 0;
            while (i < span.Length) {
                byte b = span[i];
                if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') {
                    i++;
                    continue;
                }

                return b == (byte)'{';
            }

            return false;
        }

        /// <summary>
        /// TryParseEnvelope from UTF-8 JSON object
        /// </summary>
        /// <param name="utf8"></param>
        /// <param name="env"></param>
        /// <returns></returns>
        private static bool TryParseEnvelope(ReadOnlyMemory<byte> utf8, out WebSocketEnvelope env) {
            env = default;

            try {
                Utf8JsonReader reader = new(utf8.Span, isFinalBlock: true, state: default);

                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) {
                    return false;
                }

                string op = "";
                string? id = null;
                ReadOnlyMemory<byte> payloadUtf8 = default;

                while (reader.Read()) {
                    if (reader.TokenType == JsonTokenType.EndObject) {
                        env = new WebSocketEnvelope(op, id, true, utf8, payloadUtf8);
                        return true;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName) {
                        return false;
                    }

                    bool isOp = reader.ValueTextEquals("op");
                    bool isId = reader.ValueTextEquals("id");
                    bool isPayload = reader.ValueTextEquals("payload");

                    if (!reader.Read()) {
                        return false;
                    }

                    if (isOp) {
                        if (reader.TokenType == JsonTokenType.String) {
                            op = reader.GetString() ?? "";
                        }
                        else {
                            SkipValue(ref reader);
                        }
                    }
                    else if (isId) {
                        id = GetCurrentValueAsString(utf8, ref reader);
                    }
                    else if (isPayload) {
                        payloadUtf8 = GetCurrentValueSlice(utf8, ref reader);
                    }
                    else {
                        SkipValue(ref reader);
                    }
                }

                return false;
            }
            catch (JsonException) {
                return false;
            }
        }

        /// <summary>
        /// Skip current JSON value
        /// </summary>
        /// <param name="reader"></param>
        private static void SkipValue(ref Utf8JsonReader reader) {
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) {
                reader.Skip();
            }
        }

        /// <summary>
        /// Return current JSON value as raw UTF-8 slice
        /// </summary>
        /// <param name="source"></param>
        /// <param name="reader"></param>
        /// <returns></returns>
        private static ReadOnlyMemory<byte> GetCurrentValueSlice(ReadOnlyMemory<byte> source, ref Utf8JsonReader reader) {
            int start = checked((int)reader.TokenStartIndex);

            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) {
                reader.Skip();
            }

            int end = checked((int)reader.BytesConsumed);
            return source.Slice(start, end - start);
        }

        /// <summary>
        /// Return current JSON scalar as string
        /// </summary>
        /// <param name="source"></param>
        /// <param name="reader"></param>
        /// <returns></returns>
        private static string? GetCurrentValueAsString(ReadOnlyMemory<byte> source, ref Utf8JsonReader reader) {
            if (reader.TokenType == JsonTokenType.String) {
                return reader.GetString();
            }

            if (reader.TokenType == JsonTokenType.Null) {
                return null;
            }

            ReadOnlyMemory<byte> raw = GetCurrentValueSlice(source, ref reader);
            return Encoding.UTF8.GetString(raw.Span);
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

        private readonly ConcurrentDictionary<Guid, HubRegistration> _registrations = new();

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

            if (conn.IsClosed) {
                return ValueTask.CompletedTask;
            }

            HubRegistration registration = EnsureRegistration(conn);
            registration.Join(room);

            HubRoom r = _rooms.GetOrAdd(room, static _ => new HubRoom());
            r.Join(conn);

            if (conn.IsClosed) {
                RemoveConnection(registration);
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Leave a Room
        /// </summary>
        /// <param name="room"></param>
        /// <param name="conn"></param>
        /// <returns></returns>
        public ValueTask LeaveAsync(string room, WebSocketConnection conn) {
            ArgumentNullException.ThrowIfNull(conn);

            RemoveFromRoom(room, conn);

            if (_registrations.TryGetValue(conn.Id, out HubRegistration? registration)) {
                registration.Leave(room);
                if (registration.Count == 0) {
                    RemoveRegistration(registration);
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

        private HubRegistration EnsureRegistration(WebSocketConnection conn) {
            while (true) {
                if (_registrations.TryGetValue(conn.Id, out HubRegistration? existing)) {
                    return existing;
                }

                HubRegistration registration = new(this, conn);
                if (_registrations.TryAdd(conn.Id, registration)) {
                    conn.Closed += registration.OnClosed;
                    return registration;
                }
            }
        }

        private void RemoveConnection(HubRegistration registration) {
            RemoveRegistration(registration);

            foreach (string room in registration.DrainRooms()) {
                RemoveFromRoom(room, registration.Connection);
            }
        }

        private void RemoveRegistration(HubRegistration registration) {
            if (_registrations.TryGetValue(registration.Connection.Id, out HubRegistration? current)
                && ReferenceEquals(current, registration)
            ) {
                _registrations.TryRemove(registration.Connection.Id, out _);
                registration.Connection.Closed -= registration.OnClosed;
            }
        }

        private void RemoveFromRoom(string room, WebSocketConnection conn) {
            if (_rooms.TryGetValue(room, out HubRoom? r) && r != null) {
                r.Leave(conn);
                if (r.Count == 0) {
                    _rooms.TryRemove(room, out _);
                }
            }
        }

        private sealed class HubRegistration {

            private readonly WebSocketHub _hub;
            private readonly ConcurrentDictionary<string, byte> _rooms = new(StringComparer.Ordinal);

            public WebSocketConnection Connection { get; }

            public int Count => _rooms.Count;

            public HubRegistration(WebSocketHub hub, WebSocketConnection connection) {
                _hub = hub;
                Connection = connection;
            }

            public void Join(string room) => _rooms[room] = 0;

            public void Leave(string room) => _rooms.TryRemove(room, out _);

            public string[] DrainRooms() {
                string[] rooms = _rooms.Keys.ToArray();
                _rooms.Clear();
                return rooms;
            }

            public void OnClosed() => _hub.RemoveConnection(this);
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
                foreach (KeyValuePair<Guid, WebSocketConnection> kv in _conns) {
                    WebSocketConnection c = kv.Value;
                    if (except != null && ReferenceEquals(c, except)) {
                        continue;
                    }
                    if (c.IsClosed) {
                        Leave(c);
                        continue;
                    }

                    try {
                        await c.SendTextAsync(text).ConfigureAwait(false);
                    }
                    catch {
                    }
                }
            }

            /// <summary>
            /// Broadcast Binary to all Clients of the current Room
            /// </summary>
            /// <param name="data"></param>
            /// <param name="except"></param>
            /// <returns></returns>
            public async ValueTask BroadcastBinaryAsync(ReadOnlyMemory<byte> data, WebSocketConnection? except) {
                foreach (KeyValuePair<Guid, WebSocketConnection> kv in _conns) {
                    WebSocketConnection c = kv.Value;
                    if (except != null && ReferenceEquals(c, except)) {
                        continue;
                    }
                    if (c.IsClosed) {
                        Leave(c);
                        continue;
                    }

                    try {
                        await c.SendBinaryAsync(data).ConfigureAwait(false);
                    }
                    catch {
                    }
                }
            }
        }
    }

    /// <summary>
    /// WebSocketHubEndpointExtensions
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
    /// <param name="RawUtf8"></param>
    /// <param name="PayloadUtf8"></param>
    public readonly record struct WebSocketEnvelope(string Op, string? Id, bool IsJson, ReadOnlyMemory<byte> RawUtf8, ReadOnlyMemory<byte> PayloadUtf8) {

        /// <summary>
        /// Get utf8 String from RawUtf8
        /// </summary>
        public string RawText => Encoding.UTF8.GetString(RawUtf8.Span);

        /// <summary>
        /// Flag
        /// </summary>
        public bool HasPayload => !PayloadUtf8.IsEmpty;

        /// <summary>
        /// Try deserialize full payload into T.
        /// Returns false if there is no payload or if deserialization fails.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public bool TryDeserializePayload<T>(out T? value, JsonSerializerOptions? options = null) {
            if (PayloadUtf8.IsEmpty) {
                value = default;
                return false;
            }

            try {
                value = JsonSerializer.Deserialize<T>(PayloadUtf8.Span, options);
                return true;
            }
            catch {
                value = default;
                return false;
            }
        }

    }

    /// <summary>
    /// WebSocketMessage
    /// </summary>
    /// <param name="Kind"></param>
    /// <param name="Utf8Text"></param>
    /// <param name="Binary"></param>
    public readonly record struct WebSocketMessage(WebSocketMessageKind Kind, ReadOnlyMemory<byte> Utf8Text, ReadOnlyMemory<byte> Binary);

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
