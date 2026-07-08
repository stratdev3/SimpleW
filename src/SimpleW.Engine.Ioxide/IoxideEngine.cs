using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SimpleW.Parsers;
using ioxide;
using ioxide.tls;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// Engine
    /// </summary>
    public sealed class IoxideEngine : ISimpleWEngine {

        /// <summary>
        /// Engine options.
        /// </summary>
        private readonly IoxideEngineOptions _config;

        /// <summary>
        /// Starting Callback
        /// </summary>
        private readonly Action<Reactor>? _onStart;

        /// <summary>
        /// Reactors
        /// </summary>
        private Reactor[] _reactors = [];

        /// <summary>
        /// Threads
        /// </summary>
        private Thread[] _threads = [];

        /// <summary>
        /// Owning server for this engine instance.
        /// </summary>
        private SimpleWServer? _server;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        private IPEndPoint? _endpoint;

        /// <summary>
        /// Buffer pool used by diagnostic parser modes.
        /// </summary>
        private ArrayPool<byte>? _bufferPool;

        /// <summary>
        /// Engine display name.
        /// </summary>
        public string Name => nameof(IoxideEngine);

        /// <summary>
        /// Gets whether this engine is configured to expose encrypted bytes.
        /// </summary>
        public bool IsEncrypted => _config.Tls != null;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        /// <param name="onStart"></param>
        public IoxideEngine(IoxideEngineOptions? config = null, Action<Reactor>? onStart = null) {
            _config = config ?? new IoxideEngineOptions();
            _onStart = onStart;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="config"></param>
        /// <param name="onStart"></param>
        public IoxideEngine(ServerConfig config, Action<Reactor>? onStart = null)
            : this(new IoxideEngineOptions { ServerConfig = config ?? throw new ArgumentNullException(nameof(config)) }, onStart) {
        }

        /// <summary>
        /// StartAsync
        /// </summary>
        /// <param name="server"></param>
        /// <param name="bufferPool"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public Task<EndPoint?> StartAsync(SimpleWServer server, ArrayPool<byte> bufferPool, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(bufferPool);

            IPEndPoint endpoint = (IPEndPoint)server.EndPoint;

            ServerConfig serverConfig = _config.ServerConfig ?? throw new InvalidOperationException("Ioxide engine ServerConfig must not be null.");
            ServerConfig config = NormalizeConfig(serverConfig);
            if (_config.Tls != null) {
                config = AddTlsPort(config, _config.TlsPort, checked((ushort)endpoint.Port));
            }
            config = config with {
                Port = checked((ushort)endpoint.Port),
                DualStack = endpoint.AddressFamily == AddressFamily.InterNetworkV6 && serverConfig.DualStack,
            };

            if (_reactors.Length != 0) {
                throw new InvalidOperationException("SimpleW.Engine.Ioxide is already running.");
            }

            _server = server;
            _bufferPool = bufferPool;
            _endpoint = endpoint;
            _reactors = new Reactor[config.ReactorCount];
            _threads = new Thread[config.ReactorCount];

            for (int i = 0; i < config.ReactorCount; i++) {
                Reactor reactor = new(i, config);
                reactor.OnStart = CreateOnStart();
                reactor.Handle = async (r, connection) => {
                    SimpleWServer? server = _server;
                    if (server == null || server.IsStopping) {
                        connection.DecRef();
                        return;
                    }

                    switch (_config.Mode) {
                        case IoxideEngineMode.RawTest:
                            await RunRawTestModeAsync(connection).ConfigureAwait(false);
                            break;

                        case IoxideEngineMode.ParserTest:
                            await RunCustomParserTestModeAsync(connection).ConfigureAwait(false);
                            break;

                        case IoxideEngineMode.HttpRequestParserRouterTest:
                            await RunHttpRequestParserRouterTestModeAsync(connection, server).ConfigureAwait(false);
                            break;

                        case IoxideEngineMode.HttpRequestParserRouterResponseTest:
                            await RunHttpRequestParserRouterResponseTestModeAsync(connection, server).ConfigureAwait(false);
                            break;

                        case IoxideEngineMode.HttpRequestParserRouterResponseDeferredFlushTest:
                            await RunHttpRequestParserRouterResponseTestModeAsync(connection, server, deferFlush: true).ConfigureAwait(false);
                            break;

                        default:
                            await RunSimpleWModeAsync(r, connection, server).ConfigureAwait(false);
                            break;
                    }
                };

                Thread thread = new(reactor.Run) {
                    Name = $"simplew-ioxide-reactor-{i}",
                    IsBackground = true,
                };

                _reactors[i] = reactor;
                _threads[i] = thread;
                thread.Start();
            }

            return Task.FromResult<EndPoint?>(endpoint);
        }

        /// <summary>
        /// Start ioxide services required by this engine, then run the user callback.
        /// </summary>
        /// <returns></returns>
        private Action<Reactor>? CreateOnStart() {
            if (_config.Tls == null) {
                return _onStart;
            }

            return reactor => {
                TlsService.Start(reactor, _config.Tls);
                _onStart?.Invoke(reactor);
            };
        }

        /// <summary>
        /// Run the real SimpleW pipeline over either plaintext ioxide or kTLS ioxide.
        /// </summary>
        /// <param name="reactor"></param>
        /// <param name="connection"></param>
        /// <param name="server"></param>
        /// <returns></returns>
        private async ValueTask RunSimpleWModeAsync(Reactor reactor, Connection connection, SimpleWServer server) {
            bool deferFlush = _config.FlushPolicy == IoxideFlushPolicy.EndOfReadBatch;
            TlsOptions? tls = _config.Tls;
            if (tls != null && connection.ListenerPort == _config.TlsPort) {
                TlsSession session;
                try {
                    session = await reactor.GetService<TlsService>().AcceptAsync(connection).ConfigureAwait(false);
                }
                catch {
                    try { connection.DecRef(); }
                    catch { }
                    throw;
                }

                IoxideTlsConnection transport;
                try {
                    transport = new IoxideTlsConnection(connection, session, _endpoint, deferFlush);
                }
                catch {
                    try { session.Dispose(); }
                    catch { }
                    try { connection.DecRef(); }
                    catch { }
                    throw;
                }

                await server.CreateSessionAsync(transport).ConfigureAwait(false);
                return;
            }

            await server.CreateSessionAsync(new IoxideConnection(connection, _endpoint, deferFlush)).ConfigureAwait(false);
        }

        /// <summary>
        /// Run raw diagnostic mode.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        private async ValueTask RunRawTestModeAsync(Connection connection) {
            IoxideConnection transport = new(connection, _endpoint);
            try {
                byte[] response = CreateTestResponse();

                while (true) {
                    var result = await transport._reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    // no parsing, just consume the request
                    while (TryConsumeHttpRequest(ref buffer)) {
                        transport._writer.Write(response);
                    }

                    transport._reader.AdvanceTo(buffer.Start, buffer.End);
                    if (transport._writer.UnflushedBytes > 0) {
                        await transport._writer.FlushAsync().ConfigureAwait(false);
                    }

                    if (result.IsCompleted) {
                        break;
                    }
                }
            }
            finally {
                transport.Dispose();
            }
        }

        /// <summary>
        /// Run parser diagnostic mode.
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        private async ValueTask RunCustomParserTestModeAsync(Connection connection) {
            IoxideConnection transport = new(connection, _endpoint);
            try {
                Dictionary<string, string> query = new();
                byte[] response = CreateTestResponse();

                while (true) {
                    var result = await transport._reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryConsumeParsedHttpRequest(ref buffer, query, _config.ParserTestMaxRequestHeaderSize, _config.ParserTestMaxRequestBodySize)) {
                        transport._writer.Write(response);
                    }

                    transport._reader.AdvanceTo(buffer.Start, buffer.End);
                    if (transport._writer.UnflushedBytes > 0) {
                        await transport._writer.FlushAsync().ConfigureAwait(false);
                    }

                    if (result.IsCompleted) {
                        break;
                    }
                }
            }
            finally {
                transport.Dispose();
            }
        }

        /// <summary>
        /// Run SimpleW HttpRequestParser + router diagnostic mode with a fixed ioxide response.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="server"></param>
        /// <returns></returns>
        private async ValueTask RunHttpRequestParserRouterTestModeAsync(Connection connection, SimpleWServer server) {
            ArrayPool<byte> bufferPool = _bufferPool ?? throw new InvalidOperationException("Ioxide engine buffer pool is not initialized.");
            IoxideConnection transport = new(connection, _endpoint);
            HttpRequestParser parser = new(bufferPool, _config.ParserTestMaxRequestHeaderSize, _config.ParserTestMaxRequestBodySize);
            HttpSession session = new(server, new IoxideDiagnosticSinkTransport(_endpoint), bufferPool);

            try {
                byte[] response = CreateTestResponse();
                HttpRequest request = session.Request;

                while (true) {
                    var result = await transport._reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (parser.TryReadHttpRequest(buffer, request, out long consumed, out _)) {
                        if (consumed <= 0) {
                            break;
                        }

                        buffer = buffer.Slice(consumed);

                        session.Response.Reset();
                        await server.Router.DispatchAsync(session).ConfigureAwait(false);

                        request.ReturnPooledBodyBuffer();
                        transport._writer.Write(response);
                    }

                    transport._reader.AdvanceTo(buffer.Start, buffer.End);
                    if (transport._writer.UnflushedBytes > 0) {
                        await transport._writer.FlushAsync().ConfigureAwait(false);
                    }

                    if (result.IsCompleted) {
                        break;
                    }
                }
            }
            finally {
                session.Request.ReturnPooledBodyBuffer();
                session.Dispose();
                transport.Dispose();
            }
        }

        /// <summary>
        /// Run SimpleW HttpRequestParser + router diagnostic mode with the real SimpleW response over ioxide.
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="server"></param>
        /// <param name="deferFlush"></param>
        /// <returns></returns>
        private async ValueTask RunHttpRequestParserRouterResponseTestModeAsync(Connection connection, SimpleWServer server, bool deferFlush = false) {
            ArrayPool<byte> bufferPool = _bufferPool ?? throw new InvalidOperationException("Ioxide engine buffer pool is not initialized.");
            IoxideConnection transport = new(connection, _endpoint, deferFlush);
            HttpRequestParser parser = new(bufferPool, _config.ParserTestMaxRequestHeaderSize, _config.ParserTestMaxRequestBodySize);
            HttpSession session = new(server, transport, bufferPool);

            try {
                HttpRequest request = session.Request;

                while (true) {
                    var result = await transport._reader.ReadAsync().ConfigureAwait(false);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (parser.TryReadHttpRequest(buffer, request, out long consumed, out _)) {
                        if (consumed <= 0) {
                            break;
                        }

                        buffer = buffer.Slice(consumed);

                        session.Response.Reset();
                        await server.Router.DispatchAsync(session).ConfigureAwait(false);

                        if (!session.Response.Sent) {
                            await session.Response.Status(500).Text("Internal Server Error").SendAsync().ConfigureAwait(false);
                        }

                        request.ReturnPooledBodyBuffer();
                    }

                    transport._reader.AdvanceTo(buffer.Start, buffer.End);
                    if (deferFlush) {
                        await transport.FlushDeferredAsync().ConfigureAwait(false);
                    }

                    if (result.IsCompleted) {
                        break;
                    }
                }
            }
            finally {
                session.Request.ReturnPooledBodyBuffer();
                session.Dispose();
            }
        }

        /// <summary>
        /// Create fixed diagnostic response.
        /// </summary>
        /// <returns></returns>
        private static byte[] CreateTestResponse() {
            string body = """{ "message": "hello world !" }""";
            return Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nConnection: keep-alive\r\nContent-Length: {body.Length}\r\n\r\n{body}");
        }

        #region custom parser

        /// <summary>
        /// TryConsumeParsedHttpRequest
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="query"></param>
        /// <param name="maxHeaderSize"></param>
        /// <param name="maxBodySize"></param>
        /// <returns></returns>
        private static bool TryConsumeParsedHttpRequest(ref ReadOnlySequence<byte> buffer, Dictionary<string, string> query, int maxHeaderSize, long maxBodySize) {
            query.Clear();

            if (buffer.IsEmpty) {
                return false;
            }

            ReadOnlySpan<byte> firstSpan = buffer.FirstSpan;
            if (firstSpan.Length >= 3 && firstSpan[0] == 0x16 && firstSpan[1] == 0x03) {
                return false;
            }

            if (!TryFindHeaderEndStrict(buffer, maxHeaderSize, out SequencePosition headerEndPos, out int headerBytesLen)) {
                return false;
            }

            ReadOnlySequence<byte> headerBlock = buffer.Slice(0, headerEndPos);
            SequenceReader<byte> reader = new(headerBlock);
            if (!TryReadLine(ref reader, out ReadOnlySequence<byte> requestLine)) {
                return false;
            }

            ReadOnlySpan<byte> requestLineSpan = ToSpanOrPooled(requestLine, out byte[]? pooledRequestLine);
            string protocol;
            try {
                if (!TryParseRequestLine(requestLineSpan, query, out _, out _, out _, out protocol, out _)) {
                    return false;
                }
            }
            finally {
                if (pooledRequestLine != null) {
                    ArrayPool<byte>.Shared.Return(pooledRequestLine);
                }
            }

            long? contentLength = null;
            bool isChunked = false;
            bool hostSeen = false;
            bool contentLengthSeen = false;
            string? host = null;

            while (TryReadLine(ref reader, out ReadOnlySequence<byte> line)) {
                if (line.Length == 0) {
                    break;
                }

                ReadOnlySpan<byte> lineSpan = ToSpanOrPooled(line, out byte[]? pooledLine);
                try {
                    if (!TryParseHeaderLine(lineSpan, out string? name, out string? value) || name == null) {
                        return false;
                    }

                    value ??= string.Empty;
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                        if (!TryParseContentLengthStrict(value.AsSpan(), out long parsedCL)) {
                            return false;
                        }
                        if (contentLengthSeen && contentLength.HasValue && contentLength.Value != parsedCL) {
                            return false;
                        }
                        contentLength = parsedCL;
                        contentLengthSeen = true;
                    }
                    else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) {
                        if (value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) {
                            isChunked = true;
                        }
                    }
                    else if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) {
                        if (hostSeen) {
                            return false;
                        }
                        host = value;
                        hostSeen = true;
                    }
                    else if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) {
                        if (!IsValidCookieContent(value)) {
                            return false;
                        }
                    }
                }
                finally {
                    if (pooledLine != null) {
                        ArrayPool<byte>.Shared.Return(pooledLine);
                    }
                }
            }

            if (protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase)) {
                if (string.IsNullOrWhiteSpace(host)) {
                    return false;
                }
                if (host.IndexOfAny(['@', '/', '\\', '?', '#']) >= 0) {
                    return false;
                }
            }

            if (isChunked && contentLengthSeen) {
                return false;
            }

            long totalConsumed = headerBytesLen;
            ReadOnlySequence<byte> bodyBuffer = buffer.Slice(headerBytesLen);
            if (isChunked) {
                if (!TryConsumeChunkedBody(bodyBuffer, maxBodySize, out long bodyConsumed)) {
                    return false;
                }
                totalConsumed += bodyConsumed;
            }
            else if (contentLength.HasValue && contentLength.Value > 0) {
                if (contentLength.Value > maxBodySize || bodyBuffer.Length < contentLength.Value) {
                    return false;
                }
                totalConsumed += contentLength.Value;
            }

            buffer = buffer.Slice(totalConsumed);
            return true;
        }

        /// <summary>
        /// TryFindHeaderEndStrict
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="maxHeaderSize"></param>
        /// <param name="endPos"></param>
        /// <param name="headerBytesLen"></param>
        /// <returns></returns>
        private static bool TryFindHeaderEndStrict(in ReadOnlySequence<byte> buffer, int maxHeaderSize, out SequencePosition endPos, out int headerBytesLen) {
            SequenceReader<byte> reader = new(buffer);
            int d = 0;
            bool pendingCr = false;

            while (true) {
                if (!reader.TryRead(out byte b)) {
                    endPos = default;
                    headerBytesLen = 0;
                    return false;
                }
                if (reader.Consumed > maxHeaderSize) {
                    endPos = default;
                    headerBytesLen = 0;
                    return false;
                }
                if (pendingCr) {
                    if (b != (byte)'\n') {
                        endPos = default;
                        headerBytesLen = 0;
                        return false;
                    }
                    pendingCr = false;
                }
                else if (b == (byte)'\n') {
                    endPos = default;
                    headerBytesLen = 0;
                    return false;
                }
                if (b == (byte)'\r') {
                    pendingCr = true;
                }

                d = d switch {
                    0 => b == (byte)'\r' ? 1 : 0,
                    1 => b == (byte)'\n' ? 2 : (b == (byte)'\r' ? 1 : 0),
                    2 => b == (byte)'\r' ? 3 : 0,
                    3 => b == (byte)'\n' ? 4 : (b == (byte)'\r' ? 1 : 0),
                    _ => d
                };

                if (d == 4) {
                    endPos = reader.Position;
                    headerBytesLen = (int)reader.Consumed;
                    return true;
                }
            }
        }

        /// <summary>
        /// TryReadLine
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="line"></param>
        /// <returns></returns>
        private static bool TryReadLine(ref SequenceReader<byte> reader, out ReadOnlySequence<byte> line) {
            ReadOnlySpan<byte> crlf = [(byte)'\r', (byte)'\n'];
            return reader.TryReadTo(out line, crlf, advancePastDelimiter: true);
        }

        /// <summary>
        /// ToSpanOrPooled
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="pooled"></param>
        /// <returns></returns>
        private static ReadOnlySpan<byte> ToSpanOrPooled(in ReadOnlySequence<byte> seq, out byte[]? pooled) {
            if (seq.IsSingleSegment) {
                pooled = null;
                return seq.FirstSpan;
            }
            int len = checked((int)seq.Length);
            pooled = ArrayPool<byte>.Shared.Rent(len);
            seq.CopyTo(pooled);
            return pooled.AsSpan(0, len);
        }

        /// <summary>
        /// TryParseRequestLine
        /// </summary>
        /// <param name="lineSpan"></param>
        /// <param name="query"></param>
        /// <param name="method"></param>
        /// <param name="rawTarget"></param>
        /// <param name="path"></param>
        /// <param name="protocol"></param>
        /// <param name="queryString"></param>
        /// <returns></returns>
        private static bool TryParseRequestLine(ReadOnlySpan<byte> lineSpan, Dictionary<string, string> query, out string method, out string rawTarget, out string path, out string protocol, out string queryString) {
            method = rawTarget = protocol = string.Empty;
            path = queryString = string.Empty;
            ReadOnlySpan<byte> querySpan = default;

            int firstSpace = lineSpan.IndexOf((byte)' ');
            if (firstSpace <= 0) {
                return false;
            }
            int secondSpace = lineSpan.Slice(firstSpace + 1).IndexOf((byte)' ');
            if (secondSpace < 0) {
                return false;
            }
            secondSpace += firstSpace + 1;

            ReadOnlySpan<byte> methodSpan = lineSpan.Slice(0, firstSpace);
            ReadOnlySpan<byte> targetSpan = lineSpan.Slice(firstSpace + 1, secondSpace - firstSpace - 1);
            ReadOnlySpan<byte> protocolSpan = TrimAsciiWhitespace(lineSpan.Slice(secondSpace + 1));
            if (targetSpan.Length == 0 || targetSpan[0] != (byte)'/' || protocolSpan.Length == 0) {
                return false;
            }
            foreach (byte b in targetSpan) {
                if (b <= 0x1F || b == 0x7F || b >= 0x80) {
                    return false;
                }
            }

            method = Encoding.ASCII.GetString(methodSpan);
            protocol = Encoding.ASCII.GetString(protocolSpan);
            int qIndex = targetSpan.IndexOf((byte)'?');
            ReadOnlySpan<byte> pathSpan = qIndex >= 0 ? targetSpan.Slice(0, qIndex) : targetSpan;
            if (qIndex >= 0 && qIndex + 1 < targetSpan.Length) {
                querySpan = targetSpan.Slice(qIndex + 1);
            }
            rawTarget = Encoding.ASCII.GetString(targetSpan);
            path = DecodePathBytes(pathSpan);
            queryString = querySpan.Length == 0 ? string.Empty : Encoding.ASCII.GetString(querySpan);
            ParseQueryString(querySpan, query);
            return protocol.Equals("HTTP/1.0", StringComparison.OrdinalIgnoreCase)
                || protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase)
                || protocol.StartsWith("HTTP/1.", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// TryParseHeaderLine
        /// </summary>
        /// <param name="lineSpan"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseHeaderLine(ReadOnlySpan<byte> lineSpan, out string? name, out string? value) {
            name = null;
            value = null;
            int colonIndex = lineSpan.IndexOf((byte)':');
            if (colonIndex <= 0) {
                return false;
            }
            ReadOnlySpan<byte> nameSpan = lineSpan.Slice(0, colonIndex);
            foreach (byte b in nameSpan) {
                if (b >= 128 || !IsTChar(b)) {
                    return false;
                }
            }
            ReadOnlySpan<byte> valueSpan = TrimAsciiWhitespace(lineSpan.Slice(colonIndex + 1));
            name = Encoding.ASCII.GetString(nameSpan);
            value = valueSpan.Length > 0 ? Encoding.ASCII.GetString(valueSpan) : string.Empty;
            return true;
        }

        /// <summary>
        /// ParseQueryString
        /// </summary>
        /// <param name="span"></param>
        /// <param name="dict"></param>
        private static void ParseQueryString(ReadOnlySpan<byte> span, Dictionary<string, string> dict) {
            while (!span.IsEmpty) {
                int amp = span.IndexOf((byte)'&');
                ReadOnlySpan<byte> pair = amp >= 0 ? span[..amp] : span;
                if (!pair.IsEmpty) {
                    int eq = pair.IndexOf((byte)'=');
                    if (eq > 0) {
                        dict[DecodeQueryComponentBytes(pair[..eq])] = DecodeQueryComponentBytes(pair[(eq + 1)..]);
                    }
                    else if (eq < 0) {
                        dict[DecodeQueryComponentBytes(pair)] = string.Empty;
                    }
                }
                if (amp < 0) {
                    break;
                }
                span = span[(amp + 1)..];
            }
        }

        /// <summary>
        /// TryConsumeChunkedBody
        /// </summary>
        /// <param name="bodyBuffer"></param>
        /// <param name="maxBodySize"></param>
        /// <param name="totalConsumed"></param>
        /// <returns></returns>
        private static bool TryConsumeChunkedBody(in ReadOnlySequence<byte> bodyBuffer, long maxBodySize, out long totalConsumed) {
            totalConsumed = 0;
            SequenceReader<byte> reader = new(bodyBuffer);
            long written = 0;

            while (true) {
                if (!TryReadLine(ref reader, out ReadOnlySequence<byte> sizeLineSeq)) {
                    return false;
                }
                ReadOnlySpan<byte> sizeLineSpan = ToSpanOrPooled(sizeLineSeq, out byte[]? pooledLine);
                int chunkSize;
                try {
                    if (!TryParseHexInt(sizeLineSpan, out chunkSize)) {
                        return false;
                    }
                }
                finally {
                    if (pooledLine != null) {
                        ArrayPool<byte>.Shared.Return(pooledLine);
                    }
                }
                if (chunkSize < 0) {
                    return false;
                }
                if (chunkSize == 0) {
                    while (true) {
                        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> trailerLine)) {
                            return false;
                        }
                        if (trailerLine.Length == 0) {
                            totalConsumed = reader.Consumed;
                            return true;
                        }
                    }
                }
                written += chunkSize;
                if (written > maxBodySize || reader.Remaining < chunkSize + 2) {
                    return false;
                }
                reader.Advance(chunkSize);
                if (!reader.TryRead(out byte cr) || !reader.TryRead(out byte lf) || cr != (byte)'\r' || lf != (byte)'\n') {
                    return false;
                }
            }
        }

        /// <summary>
        /// TryParseContentLengthStrict
        /// </summary>
        /// <param name="s"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseContentLengthStrict(ReadOnlySpan<char> s, out long value) {
            value = 0;
            if (s.Length == 0 || s[0] == '+' || s[0] == '-') {
                return false;
            }
            long acc = 0;
            foreach (char ch in s) {
                if (ch < '0' || ch > '9') {
                    return false;
                }
                int digit = ch - '0';
                if (acc > (long.MaxValue - digit) / 10) {
                    return false;
                }
                acc = acc * 10 + digit;
            }
            value = acc;
            return true;
        }

        /// <summary>
        /// TryParseHexInt
        /// </summary>
        /// <param name="span"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseHexInt(ReadOnlySpan<byte> span, out int value) {
            value = 0;
            int semicolon = span.IndexOf((byte)';');
            if (semicolon >= 0) {
                span = span.Slice(0, semicolon);
            }
            span = TrimAsciiWhitespace(span);
            if (span.Length == 0) {
                return false;
            }
            int result = 0;
            foreach (byte b in span) {
                int digit;
                if (b is >= (byte)'0' and <= (byte)'9') {
                    digit = b - (byte)'0';
                }
                else if (b is >= (byte)'a' and <= (byte)'f') {
                    digit = 10 + (b - (byte)'a');
                }
                else if (b is >= (byte)'A' and <= (byte)'F') {
                    digit = 10 + (b - (byte)'A');
                }
                else {
                    return false;
                }
                result = (result << 4) + digit;
            }
            value = result;
            return true;
        }

        /// <summary>
        /// TrimAsciiWhitespace
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
        private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> span) {
            int start = 0;
            int end = span.Length - 1;
            while (start <= end && IsAsciiWhitespace(span[start])) {
                start++;
            }
            while (end >= start && IsAsciiWhitespace(span[end])) {
                end--;
            }
            return span.Slice(start, end - start + 1);
        }

        private static bool IsAsciiWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

        private static bool IsTChar(byte b) => b is >= (byte)'0' and <= (byte)'9'
            || b is >= (byte)'A' and <= (byte)'Z'
            || b is >= (byte)'a' and <= (byte)'z'
            || b is (byte)'!' or (byte)'#' or (byte)'$' or (byte)'%' or (byte)'&' or (byte)'\'' or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~';

        private static bool IsValidCookieContent(string value) {
            foreach (char c in value) {
                if (c <= 0x1F || c == 0x7F) {
                    return false;
                }
            }
            return true;
        }

        private static string DecodePathBytes(ReadOnlySpan<byte> span) {
            if (span.Length == 0) {
                return string.Empty;
            }
            if (span.IndexOf((byte)'%') < 0) {
                return Encoding.ASCII.GetString(span);
            }
            string s = Encoding.ASCII.GetString(span);
            try { return Uri.UnescapeDataString(s); }
            catch { return s; }
        }

        private static string DecodeQueryComponentBytes(ReadOnlySpan<byte> span) {
            int pct = span.IndexOf((byte)'%');
            int plus = span.IndexOf((byte)'+');
            if (pct < 0 && plus < 0) {
                return Encoding.ASCII.GetString(span);
            }
            string s = Encoding.ASCII.GetString(span);
            if (plus >= 0) {
                s = s.Replace('+', ' ');
            }
            try { return Uri.UnescapeDataString(s); }
            catch { return s; }
        }

        #endregion custom parser

        /// <summary>
        /// TryConsumeHttpRequest
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        private static bool TryConsumeHttpRequest(ref ReadOnlySequence<byte> buffer) {
            SequenceReader<byte> reader = new(buffer);
            ReadOnlySpan<byte> headerEnd = [ (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' ];

            while (!reader.End) {
                if (reader.IsNext(headerEnd, advancePast: true)) {
                    buffer = buffer.Slice(reader.Position);
                    return true;
                }

                reader.Advance(1);
            }

            return false;
        }

        /// <summary>
        /// Normalize ioxide server settings before reactors are created.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        private static ServerConfig NormalizeConfig(ServerConfig config) {
            if (config.ReactorCount <= 0) {
                return config with { ReactorCount = Environment.ProcessorCount };
            }

            return config;
        }

        /// <summary>
        /// Ensure the TLS listener port is part of the ioxide multi-port configuration.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="tlsPort"></param>
        /// <param name="mainPort"></param>
        /// <returns></returns>
        private static ServerConfig AddTlsPort(ServerConfig config, ushort tlsPort, ushort mainPort) {
            if (tlsPort == 0) {
                throw new ArgumentOutOfRangeException(nameof(tlsPort), "TLS port must be greater than 0.");
            }
            if (tlsPort == mainPort || config.ExtraPorts.Contains(tlsPort)) {
                return config;
            }

            ushort[] ports = new ushort[config.ExtraPorts.Length + 1];
            Array.Copy(config.ExtraPorts, ports, config.ExtraPorts.Length);
            ports[^1] = tlsPort;
            return config with { ExtraPorts = ports };
        }

        /// <summary>
        /// StopAsync
        /// </summary>
        /// <param name="server"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default) {
            Reactor[] reactors = _reactors;
            Thread[] threads = _threads;
            _reactors = [];
            _threads = [];
            _server = null;
            _bufferPool = null;
            _endpoint = null;

            foreach (Reactor reactor in reactors) {
                try { reactor.Stop(); }
                catch { }
            }

            foreach (Thread thread in threads) {
                try {
                    if (!ReferenceEquals(thread, Thread.CurrentThread) && thread.IsAlive) {
                        thread.Join(TimeSpan.FromSeconds(5));
                    }
                }
                catch { }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            Reactor[] reactors = _reactors;
            Thread[] threads = _threads;
            _reactors = [];
            _threads = [];
            _server = null;
            _endpoint = null;

            foreach (Reactor reactor in reactors) {
                try { reactor.Stop(); }
                catch { }
            }

            foreach (Thread thread in threads) {
                try {
                    if (!ReferenceEquals(thread, Thread.CurrentThread) && thread.IsAlive) {
                        thread.Join(TimeSpan.FromSeconds(5));
                    }
                }
                catch { }
            }
        }

    }

    /// <summary>
    /// Connection
    /// </summary>
    internal sealed class IoxideConnection : ISimpleWEngine, ISimpleWTransportInput, ISimpleWTransportOutput, ISimpleWTransportDeferredFlushFeature {

        /// <summary>
        /// Connection
        /// </summary>
        private readonly Connection _connection;

        /// <summary>
        /// Pipe reader
        /// </summary>
        public readonly ConnectionPipeReader _reader;

        /// <summary>
        /// Pipe writer
        /// </summary>
        public readonly ConnectionPipeWriter _writer;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        private readonly EndPoint? _localEndPoint;

        /// <summary>
        /// Buffer
        /// </summary>
        private ReadOnlySequence<byte> _buffer;

        /// <summary>
        /// Flag buffer
        /// </summary>
        private bool _hasBuffer;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint => _localEndPoint;

        /// <summary>
        /// Remote endpoint when this transport owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint => null;

        /// <summary>
        /// Gets whether this transport already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted => false;

        /// <summary>
        /// Transport input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input => this;

        /// <summary>
        /// Transport output for an accepted connection.
        /// </summary>
        public ISimpleWTransportOutput Output => this;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="deferFlush"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public IoxideConnection(Connection connection, EndPoint? localEndPoint, bool deferFlush = false) {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _localEndPoint = localEndPoint;
            _deferFlush = deferFlush;
            _reader = new ConnectionPipeReader(connection);
            _writer = new ConnectionPipeWriter(connection);
        }

        #region ISimpleWTransportInput

        /// <summary>
        /// Read bytes from the transport.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            _buffer = result.Buffer;
            _hasBuffer = true;
            return new SimpleWTransportReadResult(_buffer, result.IsCompleted);
        }

        #endregion ISimpleWTransportInput

        #region ISimpleWTransportOutput

        /// <summary>
        /// State gate for output writes.
        /// </summary>
        private int _writing;

        /// <summary>
        /// Whether WriteAsync should stage bytes and let the owner flush later.
        /// </summary>
        private readonly bool _deferFlush;

        /// <summary>
        /// Whether deferred writes are waiting for a flush.
        /// </summary>
        private int _hasDeferredWrites;

        /// <summary>
        /// Writes all bytes to the transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                cancellationToken.ThrowIfCancellationRequested();
                _connection.Write(buffer.Span);
                await FlushAfterWriteAsync().ConfigureAwait(false);
                return buffer.Length;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes all provided segments to the transport.
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                ArgumentNullException.ThrowIfNull(segments);
                cancellationToken.ThrowIfCancellationRequested();

                long written = 0;
                foreach (ArraySegment<byte> segment in segments) {
                    if (segment.Array == null || segment.Count <= 0) {
                        continue;
                    }
                    _connection.Write(segment.AsSpan());
                    written += segment.Count;
                }

                await FlushAfterWriteAsync().ConfigureAwait(false);
                return written;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes both segments to the transport.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                cancellationToken.ThrowIfCancellationRequested();
                WriteToConnection(header);
                WriteToConnection(body);
                await FlushAfterWriteAsync().ConfigureAwait(false);
                return header.Count + body.Count;
            }
            finally {
                ExitWrite();
            }
        }

        private async ValueTask FlushAfterWriteAsync() {
            if (_deferFlush) {
                Volatile.Write(ref _hasDeferredWrites, 1);
                return;
            }

            await _connection.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Flushes writes staged by deferred-flush mode.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask FlushDeferredAsync(CancellationToken cancellationToken = default) {
            if (!_deferFlush || Interlocked.Exchange(ref _hasDeferredWrites, 0) == 0) {
                return;
            }

            EnterWrite();
            try {
                cancellationToken.ThrowIfCancellationRequested();
                await _connection.FlushAsync().ConfigureAwait(false);
            }
            finally {
                ExitWrite();
            }
        }

        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent WriteAsync on same transport output");
            }
        }

        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        #endregion ISimpleWTransportOutput

        #region process

        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void AdvanceTo(long consumedBytes, long examinedBytes) {
            if (!_hasBuffer) {
                return;
            }

            long length = _buffer.Length;
            long consumed = Clamp(consumedBytes, length);
            long examined = Clamp(examinedBytes, length);
            if (examined < consumed) {
                examined = consumed;
            }

            _reader.AdvanceTo(_buffer.GetPosition(consumed), _buffer.GetPosition(examined));

            if (consumed > 0) {
                _buffer = _buffer.Slice(consumed);
                _hasBuffer = !_buffer.IsEmpty;
            }
        }

        #endregion process

        #region dispose

        private int _disposed;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) {
                return;
            }

            try { _reader.Complete(); }
            catch { }
            try { _writer.Complete(); }
            catch { }
            try { _connection.MarkClosed(); }
            catch { }
            try { _connection.DecRef(); }
            catch { }
        }

        #endregion dispose

        #region helpers

        private void WriteToConnection(ArraySegment<byte> source) {
            if (source.Array == null || source.Count <= 0) {
                return;
            }

            _connection.Write(source.AsSpan());
        }

        private static long Clamp(long value, long max) {
            if (value <= 0) {
                return 0;
            }
            if (value >= max) {
                return max;
            }
            return value;
        }

        #endregion helpers

    }

    /// <summary>
    /// Sink transport used by diagnostic modes that execute SimpleW handlers without sending their built response to the client.
    /// </summary>
    internal sealed class IoxideDiagnosticSinkTransport : ISimpleWEngine, ISimpleWTransportInput, ISimpleWTransportOutput {

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        private readonly EndPoint? _localEndPoint;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint => _localEndPoint;

        /// <summary>
        /// Remote endpoint when this transport owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint => null;

        /// <summary>
        /// Gets whether this transport already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted => false;

        /// <summary>
        /// Transport input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input => this;

        /// <summary>
        /// Transport output for an accepted connection.
        /// </summary>
        public ISimpleWTransportOutput Output => this;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="localEndPoint"></param>
        public IoxideDiagnosticSinkTransport(EndPoint? localEndPoint) {
            _localEndPoint = localEndPoint;
        }

        /// <summary>
        /// Read bytes from the transport.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
            return ValueTask.FromResult(new SimpleWTransportReadResult(ReadOnlySequence<byte>.Empty, true));
        }

        /// <summary>
        /// Advance the transport input.
        /// </summary>
        /// <param name="consumedBytes"></param>
        /// <param name="examinedBytes"></param>
        public void AdvanceTo(long consumedBytes, long examinedBytes) {
        }

        /// <summary>
        /// Writes all bytes to the transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult((long)buffer.Length);
        }

        /// <summary>
        /// Writes all provided segments to the transport.
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(segments);
            cancellationToken.ThrowIfCancellationRequested();

            long written = 0;
            foreach (ArraySegment<byte> segment in segments) {
                written += segment.Count;
            }

            return ValueTask.FromResult(written);
        }

        /// <summary>
        /// Writes both segments to the transport.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult((long)header.Count + body.Count);
        }

        /// <summary>
        /// Close the transport.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Abort the transport.
        /// </summary>
        /// <param name="reset"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
        }

    }

    /// <summary>
    /// Controls when ioxide sends staged response bytes.
    /// </summary>
    public enum IoxideFlushPolicy {

        /// <summary>
        /// Flush inside each WriteAsync call.
        /// </summary>
        Immediate,

        /// <summary>
        /// Stage writes during request handling and flush once at the end of the current read batch.
        /// </summary>
        EndOfReadBatch,

    }

    /// <summary>
    /// Ioxide engine execution mode.
    /// </summary>
    public enum IoxideEngineMode {

        /// <summary>
        /// Run the full SimpleW pipeline.
        /// </summary>
        SimpleW,

        /// <summary>
        /// Diagnostic mode: only consume an HTTP header block and send a fixed response.
        /// </summary>
        RawTest,

        /// <summary>
        /// Diagnostic mode: run the SimpleW HTTP parser and send a fixed response, without routing or response building.
        /// </summary>
        ParserTest,

        /// <summary>
        /// Diagnostic mode of the simplew HttpRequestParser
        /// </summary>
        HttpRequestParserTest,

        /// <summary>
        /// Diagnostic mode: run the SimpleW HttpRequestParser and router/handler, then send a fixed prebuilt response.
        /// </summary>
        HttpRequestParserRouterTest,

        /// <summary>
        /// Diagnostic mode: run the SimpleW HttpRequestParser and router/handler, then send the real SimpleW response over ioxide.
        /// </summary>
        HttpRequestParserRouterResponseTest,

        /// <summary>
        /// Diagnostic mode: same as HttpRequestParserRouterResponseTest, but flushes once at the end of the read batch.
        /// </summary>
        HttpRequestParserRouterResponseDeferredFlushTest,

    }

}
