using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
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
        /// Local endpoint when the transport exposes one.
        /// </summary>
        public EndPoint? LocalEndPoint => _transport.LocalEndPoint;

        /// <summary>
        /// Remote endpoint when the transport exposes one.
        /// </summary>
        public EndPoint? RemoteEndPoint => _transport.RemoteEndPoint;

        /// <summary>
        /// Underlying transport.
        /// </summary>
        internal ISimpleWEngine Transport => _transport;

        /// <summary>
        /// Internal transport.
        /// </summary>
        private readonly ISimpleWEngine _transport;

        /// <summary>
        /// Optional deferred flush feature exposed by the transport output.
        /// </summary>
        private readonly ISimpleWTransportDeferredFlushFeature? _deferredFlush;

        /// <summary>
        /// Optional TLS metadata exposed by the transport.
        /// </summary>
        private readonly ISimpleWTransportTlsFeature? _tlsFeature;

        /// <summary>
        /// Array pool used for buffers.
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

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
        /// Gets the server JSON engine.
        /// </summary>
        public IJsonEngine JsonEngine => Server.JsonEngine;

        /// <summary>
        /// Current user principal.
        /// </summary>
        private HttpPrincipal _principal = HttpPrincipal.Anonymous;

        /// <summary>
        /// Indicates whether the principal has already been resolved.
        /// </summary>
        private bool _principalResolved;

        /// <summary>
        /// Current user principal.
        /// If not aleady resolved, call the Server.PrincipalResolver if defined
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
        /// Metadata attached to the currently matched handler.
        /// Empty when no metadata is defined or no route matched yet.
        /// </summary>
        public HandlerMetadataCollection Metadata { get; internal set; } = HandlerMetadataCollection.Empty;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="server"></param>
        /// <param name="transport"></param>
        /// <param name="bufferPool"></param>
        public HttpSession(SimpleWServer server, ISimpleWEngine transport, ArrayPool<byte> bufferPool) {
            Id = Guid.NewGuid();

            Server = server;
            _transport = transport;
            _deferredFlush = transport.Output as ISimpleWTransportDeferredFlushFeature;
            _tlsFeature = transport.GetFeature<ISimpleWTransportTlsFeature>();
            _bufferPool = bufferPool;

            _request = new HttpRequest(_bufferPool, server.JsonEngine, server.Options.MaxRequestHeaderSize, server.Options.MaxRequestBodySize);
            _parser = new HttpRequestParser(_bufferPool, server.Options.MaxRequestHeaderSize, server.Options.MaxRequestBodySize);
            _response = new HttpResponse(this, _bufferPool);

            Server.MarkSession(this);
        }

        #region Connect

        /// <summary>
        /// Flag to avoid multiple Connect() call
        /// </summary>
        private bool _receiving;

        /// <summary>
        /// Connect
        /// </summary>
        public void Connect() {
            if (_receiving) {
                return;
            }
            _receiving = true;
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
        /// Aborts the underlying connection.
        /// </summary>
        public ValueTask AbortConnectionAsync(bool reset = false, CancellationToken cancellationToken = default) {
            Abort();
            return _transport.AbortAsync(reset, cancellationToken);
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
                    // stop the watcher if an Abort() has been called or owned by another component
                    return;
                }

                try {
                    // FIN flag socket : selectRead + available == 0
                    if (_transport.TryCheckConnectionClosed(out bool isClosed) && isClosed) {
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

        #region tls

        /// <summary>
        /// Gets a value indicating whether this session uses an encrypted transport.
        /// </summary>
        public bool IsEncrypted => _transport.IsEncrypted;

        /// <summary>
        /// Gets the application protocol selected during the TLS handshake.
        /// </summary>
        public string? NegotiatedApplicationProtocol => _tlsFeature?.NegotiatedApplicationProtocol;

        /// <summary>
        /// Gets the subject of the authenticated client certificate in the transport's native format.
        /// </summary>
        public string? ClientCertificateSubject => _tlsFeature?.ClientCertificateSubject;

        /// <summary>
        /// Gets the email address of the authenticated client certificate when available.
        /// </summary>
        public string? ClientCertificateEmailAddress => _tlsFeature?.ClientCertificateEmailAddress;

        /// <summary>
        /// Gets whether the client certificate was authenticated, or null when the transport does not expose TLS metadata.
        /// </summary>
        public bool? IsClientCertificateAuthenticated => _tlsFeature?.IsClientCertificateAuthenticated;

        #endregion tls

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
        /// Marks whether the transport should be closed after the current response.
        /// </summary>
        public bool CloseAfterResponse { get; private set; }

        /// <summary>
        /// Main Process Loop :
        ///  - read native transport buffers
        ///  - parse directly from ReadOnlySequence byte
        ///  - advance the transport input by consumed and examined bytes
        ///  - dispatch through the HttpRouter
        ///  - enforce request header/body receive deadlines to mitigate slowloris
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

                SimpleWTransportReadResult readResult;
                try {
                    readResult = await _transport.Input.ReadAsync().ConfigureAwait(false);
                }
                catch (IOException) {
                    // client a reset/abort (consider as a normal closed)
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

                ReadOnlySequence<byte> buffer = readResult.Buffer;
                if (buffer.Length == 0) {
                    if (readResult.IsCompleted) {
                        // remote closed
                        Abort();
                        return;
                    }
                    continue;
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
                // slow request protection : if we are already in body phase, count newly observed body bytes
                if (_requestReceivePhase == RequestReceivePhase.Body) {
                    AddBodyBytesReceivedFromBuffer(buffer.Length);
                }

                #endregion read

                #region parse & process
                long consumedTotal = 0;
                long examinedTotal = buffer.Length;
                long advancedTotal = 0;
                try {

                    //
                    // parse HttpRequest loop (http pipelining support)
                    //
                    while (consumedTotal < buffer.Length) {
                        if (IsTransportOwned) {
                            return;
                        }

                        ReadOnlySequence<byte> parseBuffer = buffer.Slice(consumedTotal);
                        if (!_parser.TryReadHttpRequest(parseBuffer, _request, out long consumed, out bool foundHeaderEnd)) {
                            // slow request protection : request incomplete => detect whether we are waiting for headers or body
                            RefreshReceiveStateFromCurrentBuffer(parseBuffer.Length, foundHeaderEnd);
                            // need/wait for more data
                            break;
                        }

                        consumedTotal += consumed;
                        long consumedDelta = consumedTotal - advancedTotal;
                        if (consumedDelta > 0) {
                            // ReadAsync may return more than the current HTTP request:
                            // [ HTTP request ][ next request / websocket bytes already received ]
                            // Advance only the parsed HTTP part now, before a handler can take ownership of the transport.
                            _transport.Input.AdvanceTo(consumedDelta, consumedDelta);
                            advancedTotal = consumedTotal;
                        }

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
                                await Server.Router.DispatchAsync(this).ConfigureAwait(false);
                            }
                            else {
                                ValueTask dispatch = Server.Router.DispatchAsync(this);
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
                        if (consumedTotal < buffer.Length) {
                            StartHeadersReceiveDeadline();
                        }
                    }

                    // slow request protection: if the buffer was fully consumed, no in-flight request remains
                    if (consumedTotal == buffer.Length) {
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
                finally {
                    long consumedRemaining = consumedTotal - advancedTotal;
                    long examinedRemaining = examinedTotal - advancedTotal;
                    if (!IsTransportOwned && (consumedRemaining != 0 || examinedRemaining != 0)) {
                        _transport.Input.AdvanceTo(consumedRemaining, examinedRemaining);
                    }
                    if (!IsTransportOwned && _deferredFlush != null) {
                        await _deferredFlush.FlushDeferredAsync().ConfigureAwait(false);
                    }
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
            // reset handler metadata
            Metadata = HandlerMetadataCollection.Empty;
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
        /// Last observed transport buffer length while tracking body receive rate.
        /// </summary>
        private long _requestBodyObservedBufferLength;

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
        private void StartBodyReceiveRateTracking(long observedBufferLength = 0) {
            _requestReceivePhase = RequestReceivePhase.Body;
            _requestReceiveDeadlineTick = 0;

            _requestBodyStartTick = Environment.TickCount64;
            _requestBodyBytesReceived = 0;
            _requestBodyObservedBufferLength = observedBufferLength;
        }

        /// <summary>
        /// Clear current request receive tracking.
        /// </summary>
        private void ResetReceiveDeadline() {
            _requestReceivePhase = RequestReceivePhase.None;
            _requestReceiveDeadlineTick = 0;
            _requestBodyStartTick = 0;
            _requestBodyBytesReceived = 0;
            _requestBodyObservedBufferLength = 0;
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
        /// <param name="available"></param>
        /// <param name="foundHeaderEnd"></param>
        private void RefreshReceiveStateFromCurrentBuffer(long available, bool foundHeaderEnd) {
            if (available <= 0) {
                ResetReceiveDeadline();
                return;
            }
            if (foundHeaderEnd) {
                if (_requestReceivePhase != RequestReceivePhase.Body) {
                    StartBodyReceiveRateTracking(available);
                }
            }
            else {
                if (_requestReceivePhase != RequestReceivePhase.Headers) {
                    StartHeadersReceiveDeadline();
                }
            }
        }

        /// <summary>
        /// Add newly observed body bytes to the current body rate tracker.
        /// </summary>
        /// <param name="currentBufferLength"></param>
        private void AddBodyBytesReceivedFromBuffer(long currentBufferLength) {
            long delta = currentBufferLength - _requestBodyObservedBufferLength;
            if (delta <= 0) {
                return;
            }

            _requestBodyBytesReceived += delta;
            _requestBodyObservedBufferLength = currentBufferLength;
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
        /// SendAsync to transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer) {
            try {
                _response.BytesSent += await _transport.Output.WriteAsync(buffer).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (IOException) {
                Abort();
            }
        }

        /// <summary>
        /// SendAsync to transport.
        /// </summary>
        /// <param name="segments"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte>[] segments) {
            try {
                _response.BytesSent += await _transport.Output.WriteAsync(segments).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (IOException) {
                Abort();
            }
        }

        /// <summary>
        /// SendAsync to transport.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte> header, ArraySegment<byte> body) {
            try {
                _response.BytesSent += await _transport.Output.WriteAsync(header, body).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (IOException) {
                Abort();
            }
        }

        /// <summary>
        /// SendAsync to transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte> buffer) {
            try {
                _response.BytesSent += await _transport.Output.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) {
                Abort();
            }
            catch (IOException) {
                Abort();
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

            try { _transport.Dispose(); } catch { }

            try { _abortCts.Dispose(); } catch { }
        }

        #endregion IDisposable

        #region helper

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
        /// Gets a value indicating whether observability features are enabled.
        /// </summary>
        private bool IsObservability => Server.IsTelemetryEnabled || Log.IsEnabledFor(LogLevel.Error);

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
