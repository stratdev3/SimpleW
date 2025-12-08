using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;


namespace SimpleW {

    /// <summary>
    /// SimpleW
    /// </summary>
    public class SimpleW {

        /// <summary>
        /// Router
        /// </summary>
        public HttpRouter Router { get; private set; }

        #region constructor

        /// <summary>
        /// Initialize TCP server with a given IP address and port number
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        public SimpleW(IPAddress address, int port) : this(new IPEndPoint(address, port)) { }

        /// <summary>
        /// Initialize TCP server with a given IP endpoint
        /// </summary>
        /// <param name="endpoint">IP endpoint</param>
        public SimpleW(EndPoint endpoint) {
            EndPoint = endpoint;
            Router = new HttpRouter();
        }

        #endregion constructor

        #region sslcontext

        /// <summary>
        /// SslContext
        /// </summary>
        public SslContext? SslContext { get; private set; }

        /// <summary>
        /// Add SslContext
        /// </summary>
        /// <param name="sslContext"></param>
        /// <returns></returns>
        public SimpleW UseHttps(SslContext sslContext) {
            SslContext = sslContext;
            return this;
        }

        #endregion sslcontext

        #region actions

        /// <summary>
        /// Is the server started?
        /// </summary>
        public bool IsStarted { get; private set; } = false;

        /// <summary>
        /// Is the server currently stopping?
        /// </summary>
        public bool IsStopping { get; private set; } = false;

        /// <summary>
        /// Lifetime CTS (server internal)
        /// </summary>
        private CancellationTokenSource? _lifetimeCts;

        /// <summary>
        /// Task main server loop
        /// </summary>
        private Task? _lifetimeTask;

        /// <summary>
        /// Start the server (not blocking)
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
        /// <example>await server.StartAsync(appLifetime.ApplicationStopping);</example>
        public Task StartAsync(CancellationToken cancellationToken = default) {
            if (IsStarted) {
                return Task.CompletedTask;
            }

            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            IsStarted = true;
            IsStopping = false;

            ListenSocket();
            StartSessionTimeoutTimer();

            _lifetimeTask = WaitForCancellationAsync(_lifetimeCts.Token);

            // not blocking
            return Task.CompletedTask;
        }

        /// <summary>
        /// Run the server (blocking)
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <example>await server.RunAsync(cts.Token);</example>
        public async Task RunAsync(CancellationToken cancellationToken = default) {
            await StartAsync(cancellationToken).ConfigureAwait(false);
            if (_lifetimeTask != null) {
                // blocking
                await _lifetimeTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stop Server
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync() {
            if (!IsStarted || IsStopping) {
                return;
            }

            IsStopping = true;

            try {
                _lifetimeCts?.Cancel();
            }
            catch { }

            if (_lifetimeTask != null) {
                try {
                    await _lifetimeTask.ConfigureAwait(false);
                }
                catch { }
            }
        }

        /// <summary>
        /// Wait for cancellation to stop the server
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task WaitForCancellationAsync(CancellationToken cancellationToken) {
            try {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // normal
            }
            finally {
                try {
                    _listenSocket?.Close();
                    _listenSocket?.Dispose();
                }
                catch { }
                finally {
                    _listenSocket = null;
                }

                // close all active connections
                foreach (var session in Sessions.Values) {
                    try { session.Dispose(); } catch { }
                }
                Sessions.Clear();

                // stop idle timer
                _sessionTimeoutTimer?.Dispose();
                _sessionTimeoutTimer = null;

                // acceptors
                foreach (var e in _acceptorEventArgs) {
                    try { e.Completed -= OnAcceptSocketCompleted; } catch { }
                    try { e.Dispose(); }  catch { }
                }
                _acceptorEventArgs.Clear();

                // reset state
                IsStarted = false;
                IsStopping = false;

                _lifetimeCts?.Dispose();
                _lifetimeCts = null;
                _lifetimeTask = null;
            }
        }

        /// <summary>
        /// Handle error notification
        /// </summary>
        /// <param name="error">Socket error code</param>
        protected virtual void OnError(SocketError error) { }

        #endregion actions

        #region security

        /// <summary>
        /// Max size of request headers in bytes (default: 64 KB)
        /// </summary>
        public int MaxRequestHeaderSize { get; set; } = 64 * 1024;

        /// <summary>
        /// Max size of request body in bytes (default: 10 MB)
        /// </summary>
        public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;

        #endregion security

        #region func

        /// <summary>
        /// Add Func content for GET request
        /// </summary>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public SimpleW MapGet(string path, HttpHandler handler) {
            Router.MapGet(path, handler);
            return this;
        }

        /// <summary>
        /// Add Func content for POST request
        /// </summary>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public SimpleW MapPost(string path, HttpHandler handler) {
            Router.MapPost(path, handler);
            return this;
        }

        #endregion func

        #region network

        /// <summary>
        /// BufferPool
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Listen Endpoint
        /// </summary>
        public EndPoint EndPoint { get; private set; }

        /// <summary>
        /// Is this server listening on a Unix Domain Socket?
        /// </summary>
        public bool IsUnixDomainSocket => EndPoint is UnixDomainSocketEndPoint;

        /// <summary>
        /// Listen Address
        /// </summary>
        public IPAddress? Address => (EndPoint as IPEndPoint)?.Address;

        /// <summary>
        /// Listen Port
        /// </summary>
        public int Port => (EndPoint as IPEndPoint)?.Port ?? 0;

        /// <summary>
        /// Listen Socket
        /// </summary>
        private Socket? _listenSocket;

        /// <summary>
        /// SocketAsyncEventArgs list
        /// </summary>
        private readonly List<SocketAsyncEventArgs> _acceptorEventArgs = new();

        #region options

        /// <summary>
        /// Option: acceptor backlog size
        /// </summary>
        /// <remarks>
        /// This option will set the maximum length of the pending connections queue.
        /// </remarks>
        public int OptionListenSocketBacklog { get; set; } = 1024;

        /// <summary>
        /// Option: dual mode socket
        /// </summary>
        /// <remarks>
        /// Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
        /// Will work only if socket is bound on IPv6 address.
        /// </remarks>
        public bool OptionDualMode { get; set; }

        /// <summary>
        /// Option: no delay
        /// </summary>
        /// <remarks>
        /// This option will enable/disable Nagle's algorithm for TCP protocol
        /// </remarks>
        public bool OptionNoDelay { get; set; }

        /// <summary>
        /// Option: reuse address
        /// </summary>
        /// <remarks>
        /// This option will enable/disable SO_REUSEADDR if the OS support this feature
        /// </remarks>
        public bool OptionReuseAddress { get; set; }

        /// <summary>
        /// Option: enables a socket to be bound for exclusive access
        /// </summary>
        /// <remarks>
        /// This option will enable/disable SO_EXCLUSIVEADDRUSE if the OS support this feature
        /// </remarks>
        public bool OptionExclusiveAddressUse { get; set; }

        /// <summary>
        /// Option: reuse port
        /// </summary>
        /// <remarks>
        /// This option will enable SO_REUSEPORT if the OS support this feature (linux only)
        /// </remarks>
        public bool OptionReusePort { get; set; }

        /// <summary>
        /// Option : run accept socket per core
        /// </summary>
        /// <remarks>
        /// This option will run the accept socket on each machine's core
        /// </remarks>
        public bool OptionRunAcceptSocketPerCore { get; set; }

        /// <summary>
        /// Option: keep alive
        /// </summary>
        /// <remarks>
        /// This option will setup SO_KEEPALIVE if the OS support this feature
        /// </remarks>
        public bool OptionKeepAlive { get; set; }

        /// <summary>
        /// Option: TCP keep alive time
        /// </summary>
        /// <remarks>
        /// The number of seconds a TCP connection will remain alive/idle before keepalive probes are sent to the remote
        /// </remarks>
        public int OptionTcpKeepAliveTime { get; set; } = -1;

        /// <summary>
        /// Option: TCP keep alive interval
        /// </summary>
        /// <remarks>
        /// The number of seconds a TCP connection will wait for a keepalive response before sending another keepalive probe
        /// </remarks>
        public int OptionTcpKeepAliveInterval { get; set; } = -1;

        /// <summary>
        /// Option: TCP keep alive retry count
        /// </summary>
        /// <remarks>
        /// The number of TCP keep alive probes that will be sent before the connection is terminated
        /// </remarks>
        public int OptionTcpKeepAliveRetryCount { get; set; } = -1;

        /// <summary>
        /// Receive Strategy
        /// </summary>
        /// <remarks>
        /// Strategy on How to read received data
        /// </remarks>
        public ReceivedStrategy OptionReceiveStrategy { get; set; } = ReceivedStrategy.ReceiveLoopBuffer;

        #endregion options

        /// <summary>
        /// Create Socket
        /// </summary>
        /// <returns>Socket object</returns>
        protected virtual Socket CreateSocket() {
            ProtocolType protocolType = IsUnixDomainSocket ? ProtocolType.IP : ProtocolType.Tcp;
            return new Socket(EndPoint.AddressFamily, SocketType.Stream, protocolType);
        }

        /// <summary>
        /// Listen to Socket
        /// </summary>
        private void ListenSocket() {

            // sanity checks
            if (OptionReuseAddress && OptionExclusiveAddressUse) {
                throw new ArgumentException($"{nameof(OptionReuseAddress)} and {nameof(OptionExclusiveAddressUse)} are mutually exclusives.");
            }
            if (OptionReusePort) {
                if (!OperatingSystem.IsLinux()) {
                    throw new PlatformNotSupportedException($"{nameof(OptionReusePort)} is only supported on Linux.");
                }
                if (!OptionRunAcceptSocketPerCore) {
                    throw new ArgumentException($"{nameof(OptionReusePort)} is only useful on linux when {nameof(OptionRunAcceptSocketPerCore)} is enable.");
                }
            }
            if (SslContext != null && OptionReceiveStrategy == ReceivedStrategy.SocketEventArgs) {
                throw new ArgumentException($"{nameof(ReceivedStrategy.SocketEventArgs)} strategy is not compatible with Https.");
            }

            // create socket
            _listenSocket = CreateSocket();

            // option: reuse address
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, OptionReuseAddress);
            // option: exclusive address use
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, OptionExclusiveAddressUse);
            // option: reuse port
            if (OptionReusePort) {
                _listenSocket.EnableReusePort();
            }
            // option: dual mode (this option must be applied before listening)
            if (EndPoint.AddressFamily == AddressFamily.InterNetworkV6) {
                _listenSocket.DualMode = OptionDualMode;
            }

            // bind socket to endpoint
            _listenSocket.Bind(EndPoint);
            // refresh the endpoint property based on the actual endpoint created
            EndPoint = _listenSocket.LocalEndPoint!;
            // start listen to the socket with the given accepting backlog size
            _listenSocket.Listen(OptionListenSocketBacklog);

            // update the started flag
            IsStarted = true;

            int maxParalleListenSocketEventArgs = OptionRunAcceptSocketPerCore ? Environment.ProcessorCount : 1;
            for (int i = 0; i < maxParalleListenSocketEventArgs; i++) {
                // SocketAsyncEventArgs
                SocketAsyncEventArgs listenSocketEventArgs = new();
                listenSocketEventArgs.Completed += OnAcceptSocketCompleted;
                _acceptorEventArgs.Add(listenSocketEventArgs);

                // start the first accept
                AcceptSocket(listenSocketEventArgs);
            }
        }

        /// <summary>
        /// Start accept a new client connection
        /// </summary>
        /// <param name="e"></param>
        private void AcceptSocket(SocketAsyncEventArgs e) {
            if (_listenSocket == null || IsStopping) {
                return;
            }

            // socket must be cleared since the context object is being reused
            e.AcceptSocket = null;

            bool pending;
            try {
                // async accept a new client connection
                pending = _listenSocket.AcceptAsync(e);
            }
            catch (ObjectDisposedException) {
                return;
            }

            if (!pending) {
                ProcessAcceptSocket(e);
            }
        }

        /// <summary>
        /// Process accepted client connection
        /// </summary>
        private void ProcessAcceptSocket(SocketAsyncEventArgs e) {
            if (e.SocketError == SocketError.Success && e.AcceptSocket != null) {
                // handle connection : create a HttpSession
                _ = CreateSessionAsync(e.AcceptSocket);
            }
            // non disconnect errors
            else if (!(e.SocketError == SocketError.ConnectionAborted
                    || e.SocketError == SocketError.ConnectionRefused
                    || e.SocketError == SocketError.ConnectionReset
                    || e.SocketError == SocketError.OperationAborted
                    || e.SocketError == SocketError.Shutdown)
                ) {
                OnError(e.SocketError);
            }

            // accept new client (except if socket is closed)
            if (_listenSocket != null && !_listenSocket.SafeHandle.IsInvalid && !IsStopping) {
                AcceptSocket(e);
            }
        }

        /// <summary>
        /// This method is the callback method associated with Socket.AcceptAsync()
        /// operations and is invoked when an accept operation is complete
        /// </summary>
        private void OnAcceptSocketCompleted(object? sender, SocketAsyncEventArgs e) {
            if (_listenSocket == null || _listenSocket.SafeHandle.IsInvalid || IsStopping) {
                Console.WriteLine("OnAcceptSocketCompleted ERROR");
                return;
            }
            ProcessAcceptSocket(e);
        }

        #endregion network

        #region session

        /// <summary>
        /// Server sessions
        /// </summary>
        protected readonly ConcurrentDictionary<Guid, HttpSession> Sessions = new();

        /// <summary>
        /// Handle Connection and create HttpSession
        /// </summary>
        /// <param name="socket"></param>
        /// <returns></returns>
        private async Task CreateSessionAsync(Socket socket) {

            // context
            HttpSession connection = new(this, socket, _bufferPool, Router);
            //RegisterSession(connection);

            try {
                if (SslContext is not null) {
                    await connection.UseHttps(SslContext).ConfigureAwait(false);
                }
                connection.Connect(OptionReceiveStrategy);                  // receive data
                await connection.ProcessAsync().ConfigureAwait(false);      // handle data
            }
            catch (Exception ex) {
                Console.WriteLine($"[HTTP] Connection error: {ex.Message}");
            }
            finally {
                //UnregisterSession(connection.Id);
                connection.Dispose();
            }
        }

        /// <summary>
        /// Find a session with a given Id
        /// </summary>
        /// <param name="id">Session Id</param>
        /// <returns>Session with a given Id or null if the session it not connected</returns>
        public HttpSession? FindSession(Guid id) {
            return Sessions.TryGetValue(id, out var result) ? result : null;
        }

        /// <summary>
        /// Register a new session
        /// </summary>
        /// <param name="session">Session to register</param>
        internal void RegisterSession(HttpSession session) {
            Sessions.TryAdd(session.Id, session);
        }

        /// <summary>
        /// Unregister session by Id
        /// </summary>
        /// <param name="id">Session Id</param>
        internal void UnregisterSession(Guid id) {
            Sessions.TryRemove(id, out _);
        }

        /// <summary>
        /// Mark Session
        /// </summary>
        /// <param name="session"></param>
        public void MarkSession(HttpSession session) {
            if (OptionSessionTimeout == TimeSpan.MinValue) {
                return;
            }
            session.MarkActivity();
        }

        #region idle timeout

        /// <summary>
        /// Idle timeout (if no data received during timeout, then close connection)
        /// Set TimeSpan.MinValue to disable.
        /// </summary>
        public TimeSpan OptionSessionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Timer for Idel timeout
        /// </summary>
        private Timer? _sessionTimeoutTimer;

        /// <summary>
        /// Start Session Timeout Timer
        /// </summary>
        private void StartSessionTimeoutTimer() {
            if (OptionSessionTimeout == TimeSpan.MinValue) {
                return;
            }
            if (_sessionTimeoutTimer != null) {
                return;
            }

            double seconds = Math.Min(5, OptionSessionTimeout.TotalSeconds / 2);
            TimeSpan period = TimeSpan.FromSeconds(seconds);
            _sessionTimeoutTimer = new Timer(CheckSessionTimeout, null, period, period);
        }

        /// <summary>
        /// Close and Remote Session Timeout
        /// </summary>
        /// <param name="state"></param>
        private void CheckSessionTimeout(object? state) {
            if (!IsStarted || IsStopping) {
                return;
            }

            try {
                long now = Environment.TickCount64;
                long timeoutMs = (long)OptionSessionTimeout.TotalMilliseconds;

                foreach (var kvp in Sessions) {
                    HttpSession? session = kvp.Value;
                    if (now - session.LastActivityTick > timeoutMs) {
                        Sessions.TryRemove(session.Id, out HttpSession? s);
                        s?.Dispose();
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[SimpleW] CheckSessionTimeout error: {ex.Message}");
            }
        }

        #endregion idle timeout

        #endregion session

    }

}
