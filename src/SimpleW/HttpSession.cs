using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using SimpleW.Observability;
using SimpleW.Parsers;


namespace SimpleW {

    /// <summary>
    /// Represents an HTTP session used to receive requests from and send responses to a connected client.
    /// </summary>
    public sealed class HttpSession : IDisposable {

        /// <summary>
        /// Gets the unique identifier of this session.
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Owning <see cref="SimpleWServer"/> instance.
        /// </summary>
        public readonly SimpleWServer Server;

        /// <summary>
        /// Gets the server JSON engine.
        /// </summary>
        public IJsonEngine JsonEngine => Server.JsonEngine;

        /// <summary>
        /// Underlying client socket.
        /// </summary>
        private readonly Socket _socket;

        /// <summary>
        /// Array pool used for buffers.
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// Router used to Dispatch. requests.
        /// </summary>
        private readonly IRouter _router;

        /// <summary>
        /// Gets a value indicating whether observability features are enabled.
        /// </summary>
        private bool IsObservability => Server.IsTelemetryEnabled || Log.IsEnabledFor(LogLevel.Error);

        /// <summary>
        /// Flag to avoid multiple Connect() call
        /// </summary>
        private bool _receiving;

        /// <summary>
        /// Receive buffer.
        /// </summary>
        private byte[] _recvBuffer;

        /// <summary>
        /// Parsing buffer.
        /// </summary>
        private byte[] _parseBuffer;

        /// <summary>
        /// Number of bytes currently stored in the parse buffer.
        /// </summary>
        private int _parseBufferCount;

        /// <summary>
        /// Parser reused for this session.
        /// </summary>
        private HttpRequestParser _parser;

        /// <summary>
        /// Gets the current request.
        /// </summary>
        public HttpRequest Request => _request;

        /// <summary>
        /// Gets the current request.
        /// </summary>
        private readonly HttpRequest _request;

        /// <summary>
        /// Gets the current response.
        /// </summary>
        public HttpResponse Response => _response;

        /// <summary>
        /// Gets the current response.
        /// </summary>
        private readonly HttpResponse _response;

        /// <summary>
        /// Current user principal.
        /// </summary>
        private HttpPrincipal _principal = HttpPrincipal.Anonymous;

        /// <summary>
        /// flag for principal resolution
        /// </summary>
        private bool _principalResolved;

        /// <summary>
        /// Indicates whether the principal has alRead phase.y been resolved.
        /// </summary>
        public HttpPrincipal Principal {
            get {
                if (!_principalResolved) {
                    _principal = Server.PrincipalResolver?.Invoke(this) ?? HttpPrincipal.Anonymous;
                    _principalResolved = true;
                }
                return _principal;
            }
            set {
                _principal = value ?? HttpPrincipal.Anonymous;
                _principalResolved = true;
            }
        }

        /// <summary>
        /// Per-request transient storage shared across middlewares and handlers.
        /// Lazily allocated to keep the fast path clean when unused.
        /// </summary>
        private HttpBag? _bag;

        /// <summary>
        /// Per-request transient storage shared across middlewares/handlers.
        /// </summary>
        public HttpBag Bag {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _bag ??= new HttpBag();
        }

        /// <summary>
        /// Gets the underlying socket.
        /// </summary>
        public Socket Socket => _socket;

        /// <summary>
        /// Transport stream.
        /// </summary>
        private Stream? _transportStream;

        /// <summary>
        /// Gets the underlying transport as a <see cref="Stream"/> (<see cref="NetworkStream"/> or <see cref="SslStream"/>).
        /// Callers must not dispose this stream, as doing so would close the socket.
        /// </summary>
        public Stream TransportStream => _transportStream ??= new NetworkStream(_socket, ownsSocket: false);

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="server"></param>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        /// <param name="router"></param>
        public HttpSession(SimpleWServer server, Socket socket, ArrayPool<byte> bufferPool, IRouter router) {
            Id = Guid.NewGuid();

            Server = server;
            _socket = socket;
            _bufferPool = bufferPool;
            _router = router;

            _recvBuffer = _bufferPool.Rent(server.Options.ReceiveBufferSize);
            _parseBuffer = _bufferPool.Rent(server.Options.ReceiveBufferSize);
            _parseBufferCount = 0;

            _request = new HttpRequest(_bufferPool, server.JsonEngine, server.Options.MaxRequestHeaderSize, server.Options.MaxRequestBodySize);
            _parser = new HttpRequestParser(_bufferPool, server.Options.MaxRequestHeaderSize, server.Options.MaxRequestBodySize);
            _response = new HttpResponse(this, _bufferPool);

            SocketOptions();

            Server.MarkSession(this);
        }

        #region Connect

        /// <summary>
        /// Connect
        /// </summary>
        public void Connect() {
            if (_receiving) {
                return;
            }
            _receiving = true;
        }

        /// <summary>
        /// Configure Socket Options
        /// </summary>
        private void SocketOptions() {

            // enable=false : normal "graceful" close par défaut (send FIN and flush buffer)
            // enable=true, seconds=0 : abort and send immediate RST
            // enable=true, seconds>0 : wait flush buffer for X second, abort and send RST
            _socket.LingerState = new LingerOption(enable: false, seconds: 0);

            if (Server.Options.TcpKeepAlive) {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            if (Server.Options.TcpKeepAliveTime >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, Server.Options.TcpKeepAliveTime);
            }
            if (Server.Options.TcpKeepAliveInterval >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Server.Options.TcpKeepAliveInterval);
            }
            if (Server.Options.TcpKeepAliveRetryCount >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Server.Options.TcpKeepAliveRetryCount);
            }
            if (Server.Options.TcpNoDelay) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            }

        }

        #endregion Connect

        #region cancellationToken

        /// <summary>
        /// Cancellation token source used to signal request aborts.
        /// </summary>
        private readonly CancellationTokenSource _abortCts = new();

        /// <summary>
        /// Gets a cancellation token that is triggered when the client disconnects.
        /// Pass this token to aSynchronous path. work that should stop when the client disconnects.
        /// </summary>
        public CancellationToken RequestAborted {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                Volatile.Write(ref _requestAbortedAccessed, 1);
                TryStartDisconnectWatcherForCurrentDispatch();
                return _abortCts.Token;
            }
        }

        /// <summary>
        /// Cancels the request-aborted token.
        /// </summary>
        internal void Abort() {
            try {
                if (!_abortCts.IsCancellationRequested)
                    _abortCts.Cancel();
            }
            catch (ObjectDisposedException) {
                // ignore rare race condition
            }
        }

        /// <summary>
        /// Throws if the request has been aborted.
        /// Use this in Synchronous path.hronous handlers that should stop when the client disconnects.
        /// </summary>
        public void ThrowIfAborted() => RequestAborted.ThrowIfCancellationRequested();

        #region watcher

        /// <summary>
        /// 0/1 flag indicating whether <see cref="RequestAborted"/> has been accessed for the current request.
        /// </summary>
        private int _requestAbortedAccessed;

        /// <summary>
        /// Current handler task, set only for aSynchronous path.hronous Dispatch..
        /// </summary>
        private Task? _currentDispatchTask;

        /// <summary>
        /// 0/1 flag indicating whether the disconnect watcher has started for the current request.
        /// </summary>
        private int _disconnectWatcherStarted;

        /// <summary>
        /// Starts the disconnect watcher for the current request when needed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TryStartDisconnectWatcherForCurrentDispatch() {
            // session already dead / taken over
            if (_disposed || IsTransportOwned || _abortCts.IsCancellationRequested) {
                return;
            }
            // dispatch has not been called yet or the task is completed
            Task? t = _currentDispatchTask;
            if (t == null || t.IsCompleted) {
                return;
            }
            // start once per request
            if (Interlocked.Exchange(ref _disconnectWatcherStarted, 1) != 0) {
                return;
            }

            // fire-and-forget watcher (it exits when dispatchTask completes)
            _ = WatchDisconnectWhileAsync(t);
        }

        /// <summary>
        /// Polls for client disconnection while a handler is running.
        /// </summary>
        /// <param name="awaitedHandlerTask"></param>
        /// <returns></returns>
        private async Task WatchDisconnectWhileAsync(Task awaitedHandlerTask) {
            // only check if handler is not completed
            while (!awaitedHandlerTask.IsCompleted) {
                if (_abortCts.IsCancellationRequested || IsTransportOwned) {
                    // stop the watcher if an Abort() has been called or Transported by another process
                    return;
                }

                try {
                    // FIN flag socket : selectRead + available == 0
                    if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0) {
                        Abort();
                        return;
                    }
                }
                catch {
                    // assume dead
                    Abort();
                    return;
                }

                try {
                    await Task.Delay(Server.Options.SocketDisconnectPollInterval, _abortCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // if Abort() has been called, this Task.Delay will throw but it's what we want so silent the exception
                    return;
                }
            }
        }

        #endregion watcher

        #endregion cancellationToken

        #region ssl

        /// <summary>
        /// Gets a value indicating whether this session uses HTTPS.
        /// </summary>
        public bool IsSsl => _sslStream != null;

        /// <summary>
        /// SslStream
        /// </summary>
        private SslStream? _sslStream;

        /// <summary>
        /// Gets the client certificate when available.
        /// </summary>
        public X509Certificate2? ClientCertificate {
            get {
                if (_sslStream?.RemoteCertificate is X509Certificate2 x509) {
                    return x509;
                }
                if (_sslStream?.RemoteCertificate is X509Certificate cert) {
                    return new X509Certificate2(cert);
                }
                return null;
            }
        }

        /// <summary>
        /// Enables HTTPS for this session.
        /// </summary>
        /// <param name="sslContext"></param>
        /// <returns></returns>
        public async Task UseHttps(SslContext sslContext) {
            if (IsSsl) {
                return;
            }

            // SslStream above NetworkStream
            SslStream sslStream = new(
                innerStream: TransportStream,
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: sslContext.ClientCertificateValidation
            );

            // server side TLS Handshake
            await sslStream.AuthenticateAsServerAsync(
                serverCertificate: sslContext.Certificate,
                clientCertificateRequired: sslContext.ClientCertificateRequired,
                enabledSslProtocols: sslContext.Protocols,
                checkCertificateRevocation: sslContext.CheckCertificateRevocation
            ).ConfigureAwait(false);

            _sslStream = sslStream;
        }

        #endregion ssl

        #region handle idle/read timeouts

        /// <summary>
        /// Gets the last activity tick.
        /// </summary>
        public long LastActivityTick { get; private set; }

        /// <summary>
        /// Updates the last activity tick.
        /// </summary>
        public void MarkActivity() {
            LastActivityTick = Environment.TickCount64;
        }

        #endregion handle idle/read timeouts

        #region Process

        /// <summary>
        /// Transport ownership flag.
        /// </summary>
        private int _isTransportOwned;

        /// <summary>
        /// Gets a value indicating whether transport ownership has been taken by an external component.
        /// </summary>
        internal bool IsTransportOwned => Volatile.Read(ref _isTransportOwned) == 1;

        /// <summary>
        /// Stops HTTP parsing and transfers transport ownership.
        /// </summary>
        internal bool TryTakeTransportOwnership() => Interlocked.Exchange(ref _isTransportOwned, 1) == 0;

        /// <summary>
        /// Transport ownership flag. to close Marks the session as Read phase.y to receive data.ion after Response
        /// </summary>
        public bool CloseAfterResponse { get; private set; }

        /// <summary>
        /// Main Process Loop :
        ///  - read from Socket or SslStream to _recvBuffer
        ///  - BlockCopy in _parseBuffer
        ///  - Parse with HttpRequestParserState
        ///  - HttpRouter Dispatch
        ///  - Enforce request header/body receive deadlines to mitigate slowloris
        /// </summary>
        public async Task ProcessAsync() {

            //
            // MAIN PROCESS LOOP
            //
            while (true) {

                #region read

                if (IsTransportOwned) {
                    return;
                }

                // slow request protection : headers use an absolute timeout
                long nowTick = Environment.TickCount64;
                if (IsReceiveDeadlineExpired(nowTick)) {
                    await SendRequestTimeoutAndAbortAsync("Request headers timeout.").ConfigureAwait(false);
                    return;
                }
                // slow request protection : body uses a minimum sustained receive rate
                if (IsBodyReceiveRateTooSlow(nowTick)) {
                    await SendRequestTimeoutAndAbortAsync("Request body too slow.").ConfigureAwait(false);
                    return;
                }

                int bytesRead;
                try {
                    if (IsSsl) {
                        bytesRead = await _sslStream!.ReadAsync(_recvBuffer, 0, _recvBuffer.Length).ConfigureAwait(false);
                    }
                    else {
                        bytesRead = await _socket.ReceiveAsync(_recvBuffer.AsMemory(0, _recvBuffer.Length), SocketFlags.None).ConfigureAwait(false);
                    }
                }
                catch (IOException ex)
                        when (ex.InnerException is SocketException se
                              && (se.SocketErrorCode == SocketError.ConnectionReset
                                  || se.SocketErrorCode == SocketError.ConnectionAborted)
                    ) {
                    // client a reset/abort (consider as a normal closed)
                    Abort();
                    return;
                }
                catch (SocketException se)
                    when (se.SocketErrorCode == SocketError.ConnectionReset
                          || se.SocketErrorCode == SocketError.ConnectionAborted
                ) {
                    Abort();
                    return;
                }
                catch (ObjectDisposedException) {
                    // socket/stream closed (idle timeout or Dispose)
                    Abort();
                    return;
                }

                if (IsTransportOwned) {
                    return;
                }

                if (bytesRead == 0) {
                    // remote closed
                    Abort();
                    return;
                }

                // reset idle timer
                Server.MarkSession(this);

                // first byte timing: start request watch when we receive data for a new request
                if (IsObservability && !_requestTimingStarted) {
                    _requestStartWatch = Telemetry.GetWatch();
                    _requestTimingStarted = true;
                }

                // slow request protection : first bytes of a new request => start header deadline
                if (_requestReceivePhase == RequestReceivePhase.None) {
                    StartHeadersReceiveDeadline();
                }
                // slow request protection : if we are already in body phase, count the newly received body bytes
                if (_requestReceivePhase == RequestReceivePhase.Body) {
                    AddBodyBytesReceived(bytesRead);
                }

                // byte operations
                EnsureParseBufferCapacity(bytesRead);
                Buffer.BlockCopy(_recvBuffer, 0, _parseBuffer, _parseBufferCount, bytesRead);
                _parseBufferCount += bytesRead;

                #endregion read

                #region parse & process
                try {
                    int offset = 0;

                    //
                    // parse HttpRequest loop (http pipelining support)
                    //
                    while (true) {
                        if (IsTransportOwned) {
                            return;
                        }

                        if (!_parser.TryReadHttpRequest(new ReadOnlySequence<byte>(_parseBuffer, offset, _parseBufferCount - offset), _request, out long consumed, out bool foundHeaderEnd)) {
                            // slow request protection : request incomplete => detect whether we are waiting for headers or body
                            RefreshReceiveStateFromCurrentBuffer(offset, foundHeaderEnd);
                            // need/wait for more data
                            break;
                        }

                        offset += (int)consumed;

                        // slow request protection : current request is complete, clear deadline
                        ResetReceiveDeadline();

                        // PER-REQUEST SCOPE
                        bool hasCatched = false;
                        try {
                            PerRequestReset();

                            if (IsObservability && !_requestTimingStarted) {
                                _requestStartWatch = Telemetry.GetWatch();
                                _requestTimingStarted = true;
                            }

                            // should close connection
                            CloseAfterResponse = ShouldCloseConnection(_request);

                            if (IsTransportOwned) {
                                return;
                            }

                            if (IsObservability) {
                                _responseStartWatch = Telemetry.GetWatch();
                            }
                            if (Server.IsTelemetryEnabled) {
                                _currentActivity = Server.Telemetry?.StartActivity(this);
                            }

                            #region dispatch

                            Volatile.Write(ref _requestAbortedAccessed, 0);
                            Volatile.Write(ref _disconnectWatcherStarted, 0);
                            _currentDispatchTask = null;

                            // RequestAborted disabled
                            if (Server.Options.SocketDisconnectPollInterval == TimeSpan.Zero) {
                                await _router.DispatchAsync(this).ConfigureAwait(false);
                            }
                            else {
                                ValueTask dispatch = _router.DispatchAsync(this);
                                // sync
                                if (dispatch.IsCompletedSuccessfully) {
                                    // need to consume
                                    dispatch.GetAwaiter().GetResult();
                                }
                                // async
                                else {
                                    Task dispatchTask = dispatch.AsTask();
                                    _currentDispatchTask = dispatchTask;
                                    // RequestAborted was accessed before _currentDispatchTask was set
                                    if (Volatile.Read(ref _requestAbortedAccessed) != 0) {
                                        TryStartDisconnectWatcherForCurrentDispatch();
                                    }
                                    try {
                                        // wait handler
                                        await dispatchTask.ConfigureAwait(false);
                                    }
                                    finally {
                                        _currentDispatchTask = null;
                                    }
                                }
                            }

                            #endregion dispatch

                            if (IsTransportOwned) {
                                return;
                            }

                            // if so, then close connection
                            if (WillCloseConnection()) {
                                return;
                            }
                        }
                        catch {
                            hasCatched = true;
                            throw;
                        }
                        finally {
                            if (!hasCatched) {
                                if (!IsTransportOwned && !_response.Sent) {
                                    Exception ex = new("The request pipeline completed without sending a response. A fallback 500 Internal Server Error response has been automatically generated.");
                                    await HandleErrorResponseAsync(ex, 500, "Internal Server Error", "HTTP process");
                                }
                                else if (Server.IsTelemetryEnabled && _currentActivity != null) {
                                    Server.Telemetry?.AddRequestMetrics(this, Telemetry.ElapsedMs(_requestStartWatch, _responseStartWatch));
                                    // we must close telemetry here in this flow !! closing into NotifyResponseSent() will leak memory !!
                                    CloseAndResetTelemetryWatches();
                                }
                            }

                            _request.ReturnPooledBodyBuffer();
                        }

                        // slow request protection : if another pipelined request is already buffered, arm header deadline for it
                        if (offset < _parseBufferCount) {
                            StartHeadersReceiveDeadline();
                        }
                    }

                    // compress buffer
                    CompressParseBuffer(offset);

                    // slow request protection:  if buffer is empty after compression, no in-flight request remains
                    if (_parseBufferCount == 0) {
                        ResetReceiveDeadline();
                    }
                }
                catch (HttpRequestException ex) {
                    await HandleErrorResponseAsync(ex, ex.StatusCode, ex.StatusText, ex.DisplayName);
                    return;
                }
                catch (Exception ex) {
                    await HandleErrorResponseAsync(ex, 500, "Internal Server Error", "HTTP process");
                    return;
                }
                #endregion parse & process
            }
        }

        /// <summary>
        /// Determines whether the connection should be closed after the response.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldCloseConnection(HttpRequest request) {

            // HTTP/1.1 : keep-alive by default, except when "Connection: close"
            if (request.Protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase)) {
                return request.Headers.Connection != null && request.Headers.Connection.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // HTTP/1.0 : close by default, except when "Connection: keep-alive"
            if (request.Protocol.Equals("HTTP/1.0", StringComparison.OrdinalIgnoreCase)) {
                return request.Headers.Connection == null || request.Headers.Connection.IndexOf("keep-alive", StringComparison.OrdinalIgnoreCase) < 0;
            }

            // true to close
            return true;
        }

        /// <summary>
        /// WillCloseMarks the session as Read phase.y to receive data.ion
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool WillCloseConnection() {
            if (_abortCts.IsCancellationRequested) {
                return true;
            }
            if (_response.Sent && _response.Connection != null) {
                return _response.Connection.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return CloseAfterResponse;
        }

        /// <summary>
        /// Resets per-request state.
        /// </summary>
        private void PerRequestReset() {
            // reset response
            _response.Reset();
            // reset exception
            _pendingResponseException = null;
            // reset bag
            _bag?.Clear();
            // reset principal
            _principal = HttpPrincipal.Anonymous;
            _principalResolved = false;
        }

        #region slow request protection

        /// <summary>
        /// Receive phase for the current in-flight HTTP request.
        /// </summary>
        private enum RequestReceivePhase {
            None = 0,
            Headers = 1,
            Body = 2
        }

        /// <summary>
        /// Current receive phase for the in-flight HTTP request.
        /// None   = no request currently being assembled.
        /// Headers = waiting for the full header block.
        /// Body    = headers received, waiting for the full body.
        /// </summary>
        private RequestReceivePhase _requestReceivePhase;

        /// <summary>
        /// Absolute deadline (Environment.TickCount64) for the current receive phase.
        /// If current tick is greater than this value, the request must be aborted.
        /// </summary>
        private long _requestReceiveDeadlineTick;

        /// <summary>
        /// Tick value (Environment.TickCount64) marking when the request body
        /// reception started.
        /// </summary>
        private long _requestBodyStartTick;

        /// <summary>
        /// Total number of body bytes received since <see cref="_requestBodyStartTick"/>.
        /// </summary>
        private long _requestBodyBytesReceived;

        /// <summary>
        /// Start the deadline for receiving HTTP request headers.
        /// </summary>
        private void StartHeadersReceiveDeadline() {
            _requestReceivePhase = RequestReceivePhase.Headers;

            if (Server.Options.RequestHeadersTimeoutMs == 0) {
                _requestReceiveDeadlineTick = 0;
                return;
            }

            _requestReceiveDeadlineTick = Environment.TickCount64 + Server.Options.RequestHeadersTimeoutMs;
        }

        /// <summary>
        /// Enter body receive phase and initialize body rate tracking.
        /// </summary>
        private void StartBodyReceiveRateTracking() {
            _requestReceivePhase = RequestReceivePhase.Body;
            _requestReceiveDeadlineTick = 0;

            _requestBodyStartTick = Environment.TickCount64;
            _requestBodyBytesReceived = 0;
        }

        /// <summary>
        /// Clear current request receive tracking.
        /// </summary>
        private void ResetReceiveDeadline() {
            _requestReceivePhase = RequestReceivePhase.None;
            _requestReceiveDeadlineTick = 0;
            _requestBodyStartTick = 0;
            _requestBodyBytesReceived = 0;
        }

        /// <summary>
        /// Return true when the active header receive deadline is expired.
        /// </summary>
        /// <param name="nowTick"></param>
        /// <returns></returns>
        private bool IsReceiveDeadlineExpired(long nowTick) {
            return _requestReceiveDeadlineTick != 0 && nowTick > _requestReceiveDeadlineTick;
        }

        /// <summary>
        /// Refresh the current receive state based on buffered bytes.
        /// If headers are complete, switch to body rate tracking.
        /// Otherwise stay in header timeout mode.
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="foundHeaderEnd"></param>
        private void RefreshReceiveStateFromCurrentBuffer(int offset, bool foundHeaderEnd) {
            int available = _parseBufferCount - offset;
            if (available <= 0) {
                ResetReceiveDeadline();
                return;
            }
            if (foundHeaderEnd) {
                if (_requestReceivePhase != RequestReceivePhase.Body) {
                    StartBodyReceiveRateTracking();
                }
            }
            else {
                if (_requestReceivePhase != RequestReceivePhase.Headers) {
                    StartHeadersReceiveDeadline();
                }
            }
        }

        /// <summary>
        /// Add newly received body bytes to the current body rate tracker.
        /// </summary>
        /// <param name="bytesRead"></param>
        private void AddBodyBytesReceived(int bytesRead) {
            if (bytesRead <= 0) {
                return;
            }

            _requestBodyBytesReceived += bytesRead;
        }

        /// <summary>
        /// Return true when the current body receive rate is too slow.
        /// </summary>
        /// <param name="nowTick"></param>
        /// <returns></returns>
        private bool IsBodyReceiveRateTooSlow(long nowTick) {
            if (_requestReceivePhase != RequestReceivePhase.Body) {
                return false;
            }

            if (Server.Options.MinRequestBodyDataRateBytesPerSecond <= 0) {
                return false;
            }

            long elapsedMs = nowTick - _requestBodyStartTick;
            long effectiveMs = elapsedMs - Server.Options.RequestBodyGracePeriodMs;

            if (effectiveMs <= 0) {
                return false;
            }

            long minimumExpectedBytes = (effectiveMs * Server.Options.MinRequestBodyDataRateBytesPerSecond) / 1000;

            return _requestBodyBytesReceived < minimumExpectedBytes;
        }

        /// <summary>
        /// Send a 408 Request Timeout response and abort the current session.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private async Task SendRequestTimeoutAndAbortAsync(string message) {
            try {
                CloseAfterResponse = true;
                await _response.Status(408)
                               .AddHeader("Connection", "close")
                               .Text(message)
                               .SendAsync().ConfigureAwait(false);
                _log.Error($"close connection because {message}");
            }
            catch {
                // ignore send failure, transport may already be broken
            }
            finally {
                Abort();
            }
        }

        #endregion slow request protection

        #endregion Process

        #region SendAsync

        /// <summary>
        /// State Gate for SendAsync thread-safe
        /// </summary>
        private int _sending;

        /// <summary>
        /// Storage Segment Array for SendAsync(header, body)
        /// </summary>
        private readonly ArraySegment<byte>[] _sendSegments2 = new ArraySegment<byte>[2];

        /// <summary>
        /// SendAsync native to socket (thread safe)
        /// Lower level of sending
        /// You should call NotifyResponseSent() once you finished sending reponse
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream != null) {
                    await _sslStream.WriteAsync(buffer).ConfigureAwait(false);
                    _response.BytesSent += buffer.Length;
                }
                else {
                    int bytesSent = await _socket.SendAsync(buffer, SocketFlags.None).ConfigureAwait(false);
                    _response.BytesSent += bytesSent;
                }
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (SocketException) {
                Abort();
            }
            finally {
                Volatile.Write(ref _sending, 0);
            }
        }

        /// <summary>
        /// SendAsync to socket (thread safe)
        /// Lower level of sending
        /// You should call NotifyResponseSent() once you finished sending reponse
        /// </summary>
        /// <param name="segments"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte>[] segments) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream != null) {
                    // HTTPS : write each segment to sslStream
                    foreach (ArraySegment<byte> seg in segments) {
                        if (seg.Array == null || seg.Count == 0) {
                            continue;
                        }
                        await _sslStream.WriteAsync(seg.Array, seg.Offset, seg.Count).ConfigureAwait(false);
                        _response.BytesSent += seg.Count;
                    }
                }
                else {
                    int bytesSent = await _socket.SendAsync(segments, SocketFlags.None).ConfigureAwait(false);
                    _response.BytesSent += bytesSent;
                }
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (SocketException) {
                Abort();
            }
            finally {
                Volatile.Write(ref _sending, 0);
            }
        }

        /// <summary>
        /// SendAsync to socket (thread safe)
        /// Lower level of sending
        /// You should call NotifyResponseSent() once you finished sending reponse
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte> header, ArraySegment<byte> body) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream != null) {
                    // HTTPS : write each segment to sslStream
                    if (header.Array != null && header.Count > 0) {
                        await _sslStream.WriteAsync(header.Array, header.Offset, header.Count).ConfigureAwait(false);
                        _response.BytesSent += header.Count;
                    }
                    if (body.Array != null && body.Count > 0) {
                        await _sslStream.WriteAsync(body.Array, body.Offset, body.Count).ConfigureAwait(false);
                        _response.BytesSent += body.Count;
                    }
                }
                else {
                    _sendSegments2[0] = header;
                    _sendSegments2[1] = body;
                    int bytesSent = await _socket.SendAsync(_sendSegments2, SocketFlags.None).ConfigureAwait(false);
                    _response.BytesSent += bytesSent;
                }
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (SocketException) {
                Abort();
            }
            finally {
                Volatile.Write(ref _sending, 0);
            }
        }

        /// <summary>
        /// SendAsync to socket (thread safe)
        /// Lower level of sending
        /// You should call NotifyResponseSent() once you finished sending reponse
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask SendAsync(ArraySegment<byte> buffer) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream != null) {
                    await _sslStream.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
                    _response.BytesSent += buffer.Count;
                }
                else {
                    int bytesSent = await _socket.SendAsync(buffer.AsMemory(), SocketFlags.None).ConfigureAwait(false);
                    _response.BytesSent += bytesSent;
                }
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (SocketException) {
                Abort();
            }
            finally {
                Volatile.Write(ref _sending, 0);
            }
        }

        #endregion SendAsync

        #region IDisposable

        /// <summary>
        /// Transport ownership flag. to avoid multiple Dispose() called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;

            // ensure Abort is signaled for in-flight work
            Abort();

            try {
                if (_socket.Connected) {
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch { }

            try { _socket.Dispose(); } catch { }

            try { _sslStream?.Dispose(); } catch { }
            _sslStream = null;

            if (_recvBuffer != null) {
                _bufferPool.Return(_recvBuffer);
                _recvBuffer = null!;
            }

            if (_parseBuffer != null) {
                _bufferPool.Return(_parseBuffer);
                _parseBuffer = null!;
                _parseBufferCount = 0;
            }

            try { _abortCts.Dispose(); } catch { }
        }

        #endregion IDisposable

        #region helper

        /// <summary>
        /// Enlarge Parse Buffer if needed
        /// </summary>
        /// <param name="additionalBytes">number of bytes to add</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureParseBufferCapacity(int additionalBytes) {
            int required = _parseBufferCount + additionalBytes;
            if (_parseBuffer.Length >= required) {
                return;
            }

            int newSize = _parseBuffer.Length * 2;
            if (newSize < required) {
                newSize = required;
            }

            byte[] newBuffer = _bufferPool.Rent(newSize);
            Buffer.BlockCopy(_parseBuffer, 0, newBuffer, 0, _parseBufferCount);
            _bufferPool.Return(_parseBuffer);
            _parseBuffer = newBuffer;
        }

        /// <summary>
        /// Compacts the parse buffer after consumed bytes are removed.
        /// </summary>
        /// <param name="offset"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CompressParseBuffer(int offset) {
            if (offset <= 0) {
                return;
            }

            int remaining = _parseBufferCount - offset;
            if (remaining > 0) {
                Buffer.BlockCopy(_parseBuffer, offset, _parseBuffer, 0, remaining);
            }
            _parseBufferCount = remaining;
        }

        /// <summary>
        /// Creates a fake request for testing purposes.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        public static bool FakeHttpRequest(ref ReadOnlySequence<byte> buffer, HttpRequest request) {
            request.Reset();

            if (buffer.Length == 0) {
                return false;
            }

            request.ParserSetMethod("GET");
            request.ParserSetRawTarget("/api/test/hello");
            request.ParserSetPath("/api/test/hello");
            request.ParserSetProtocol("HTTP/1.1");
            request.ParserSetHeaders(default);

            buffer = buffer.Slice(buffer.End);
            return true;
        }

        #endregion helper

        #region ip

        /// <summary>
        /// Client IPAddress
        /// </summary>
        /// <returns></returns>
        public IPAddress? ClientIpAddress => Server.ClientIpResolver.Invoke(this);

        #endregion ip

        #region telemetry

        /// <summary>
        /// Request start: first byte received for the current request
        /// </summary>
        private long _requestStartWatch;

        /// <summary>
        /// Response start: just before dispatch (boundary between request and response)
        /// </summary>
        private long _responseStartWatch;

        /// <summary>
        /// True if we already started timing for the current request (we saw first bytes)
        /// </summary>
        private bool _requestTimingStarted;

        /// <summary>
        /// Close and Reset telemetry
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CloseAndResetTelemetryWatches() {
            Server.Telemetry?.StopActivity(_currentActivity);
            _currentActivity = null;

            _requestStartWatch = 0;
            _responseStartWatch = 0;
            _requestTimingStarted = false;
        }

        /// <summary>
        /// Current telemetry activity.
        /// </summary>
        private Activity? _currentActivity;

        /// <summary>
        /// Exception associated with the pending response, if any.
        /// </summary>
        private Exception? _pendingResponseException;

        /// <summary>
        /// Handle Error Response Async
        /// </summary>
        /// <param name="ex"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="displayName"></param>
        /// <returns></returns>
        private async ValueTask HandleErrorResponseAsync(Exception ex, int statusCode, string statusText, string displayName) {
            _pendingResponseException = ex;
            CloseAfterResponse = true;
            if (Server.IsTelemetryEnabled) {
                if (!_requestTimingStarted) {
                    _requestStartWatch = Telemetry.GetWatch();
                    _requestTimingStarted = true;
                }
                _responseStartWatch = Telemetry.GetWatch();
                _currentActivity ??= Server.Telemetry?.StartActivity(this, displayName, (statusCode >= 500));
                Server.Telemetry?.UpdateActivityAddException(_currentActivity, ex);
                Server.Telemetry?.AddRequestMetrics(this, Telemetry.ElapsedMs(_requestStartWatch, _responseStartWatch));
                await _response.Status(statusCode).Text(statusText).SendAsync().ConfigureAwait(false);
                CloseAndResetTelemetryWatches();
            }
            else {
                await _response.Status(statusCode).Text(statusText).SendAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Notifies the session that the response has been sent.
        /// </summary>
        public void NotifyResponseSent() {
            if (_pendingResponseException == null) {
                if (Log.IsEnabledFor(LogLevel.Information)) {
                    _log.Info(
                        $"{_request.Method} " +
                        $"\"{_request.Path}\" " +
                        $"{_response.StatusCode} " +
                        $"{(int)Telemetry.ElapsedMs(_requestStartWatch, Telemetry.GetWatch())}ms " +
                        $"session-{Id} " +
                        $"{ClientIpAddress} "
                    );
                }
            }
            else if (Log.IsEnabledFor(LogLevel.Error)) {
                Exception? pendingEx = _pendingResponseException;
                _pendingResponseException = null;
                _log.Error(
                    $"{_request.Method} " +
                    $"\"{_request.Path}\" " +
                    $"{_response.StatusCode} " +
                    $"{(int)Telemetry.ElapsedMs(_requestStartWatch, Telemetry.GetWatch())}ms " +
                    $"session-{Id} " +
                    $"{ClientIpAddress} " +
                    $"\"{_request.Headers.UserAgent}\"",
                    pendingEx
                );
            }
            if (!Server.IsTelemetryEnabled) {
                return;
            }
            if (_currentActivity == null) {
                return;
            }
            if (_responseStartWatch == 0) {
                return;
            }
            Server.Telemetry?.UpdateActivityAddResponse(_currentActivity, this);
            Server.Telemetry?.AddResponseMetrics(this, Telemetry.ElapsedMs(_responseStartWatch, Telemetry.GetWatch()));
        }

        #endregion telemetry

        #region logging

        /// <summary>
        /// Logger instance.
        /// </summary>
        private static readonly ILogger _log = new Logger<HttpSession>();

        #endregion logging

    }

    /// <summary>
    /// HttpRequestException
    /// </summary>
    public sealed class HttpRequestException : Exception {

        /// <summary>
        /// Gets the HTTP status code to return.
        /// </summary>
        public int StatusCode { get; }
        /// <summary>
        /// Gets the HTTP status text to return.
        /// </summary>
        public string StatusText { get; }
        /// <summary>
        /// Gets the display name used for diagnostics.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="message"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="displayName"></param>
        public HttpRequestException(string message, int statusCode, string? statusText = null, string? displayName = "HTTP Parse") : base(message) {
            StatusCode = statusCode;
            StatusText = statusText ?? HttpResponse.DefaultStatusText(statusCode);
            DisplayName = displayName ?? StatusText;
        }

    }

}
