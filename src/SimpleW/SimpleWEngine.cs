using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using SimpleW.Observability;


namespace SimpleW {

    /// <summary>
    /// Default socket-based network engine used by SimpleW.
    /// One engine instance manages one server at a time.
    /// </summary>
    public class SimpleWEngine : ISimpleWEngine {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<SimpleWEngine>();

        /// <summary>
        /// Active listen socket.
        /// </summary>
        private Socket? _listenSocket;

        /// <summary>
        /// SocketAsyncEventArgs list.
        /// </summary>
        private readonly List<SocketAsyncEventArgs> _acceptorEventArgs = new();

        /// <summary>
        /// Owning server for this engine instance.
        /// </summary>
        private SimpleWServer? _server;

        /// <summary>
        /// Engine options.
        /// </summary>
        private readonly SimpleWEngineOptions _options;

        /// <summary>
        /// Pool used by transports created by this engine while it is started.
        /// </summary>
        private ArrayPool<byte>? _bufferPool;

        /// <summary>
        /// Engine display name.
        /// </summary>
        public string Name => nameof(SimpleWEngine);

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public SimpleWEngine(SimpleWEngineOptions? options = null) {
            _options = options ?? new SimpleWEngineOptions();
        }

        /// <summary>
        /// Start the engine and begin accepting connections.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="bufferPool"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<EndPoint?> StartAsync(
            SimpleWServer server,
            ArrayPool<byte> bufferPool,
            CancellationToken cancellationToken = default
        ) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(bufferPool);

            if (_server != null && !ReferenceEquals(_server, server)) {
                throw new InvalidOperationException("This engine instance is already attached to another SimpleWServer.");
            }
            if (_listenSocket != null) {
                throw new InvalidOperationException("The engine is already started for this server.");
            }

            SimpleWEngineOptions options = _options.ValidateAndNormalize();
            _server = server;
            _bufferPool = bufferPool;

            try {
                // create socket
                _listenSocket = CreateListenSocket(server.EndPoint);

                // option: reuse address
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, options.ReuseAddress);
                // option: exclusive address use
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, options.ExclusiveAddressUse);
                // option: reuse port
                if (options.ReusePort) {
                    _listenSocket.EnableReusePort();
                }
                // option: dual mode (this option must be applied before listening)
                if (server.EndPoint.AddressFamily == AddressFamily.InterNetworkV6) {
                    _listenSocket.DualMode = options.DualMode;
                }

                // bind socket to endpoint
                _listenSocket.Bind(server.EndPoint);
                // start listen to the socket with the given accepting backlog size
                _listenSocket.Listen(options.ListenBacklog);

                // by default only run one SAEA instance but when
                // user enables AcceptPerCore, he wants better perf
                // and we force a minimum of 2 instances. The use case
                // it VPS hosting with only 1 core, 2 instances
                // can be a real perf improvement.
                int maxParallelListenSocketEventArgs = options.AcceptPerCore ? Math.Max(2, Environment.ProcessorCount) : 1;

                for (int i = 0; i < maxParallelListenSocketEventArgs; i++) {
                    // SocketAsyncEventArgs
                    SocketAsyncEventArgs listenSocketEventArgs = new();
                    listenSocketEventArgs.Completed += OnAcceptSocketCompleted;
                    _acceptorEventArgs.Add(listenSocketEventArgs);

                    // start the first accept
                    AcceptSocket(listenSocketEventArgs);
                }

                return Task.FromResult<EndPoint?>(_listenSocket.LocalEndPoint!);
            }
            catch {
                StopAndDispose();
                throw;
            }
        }

        /// <summary>
        /// Stop the engine and release its listener resources.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(server);

            if (_server != null && !ReferenceEquals(_server, server)) {
                throw new InvalidOperationException("This engine instance is attached to another SimpleWServer.");
            }

            StopAndDispose();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Create the listen socket for the given endpoint.
        /// </summary>
        /// <param name="endPoint"></param>
        /// <returns></returns>
        protected virtual Socket CreateListenSocket(EndPoint endPoint) {
            ArgumentNullException.ThrowIfNull(endPoint);

            ProtocolType protocolType = endPoint is UnixDomainSocketEndPoint ? ProtocolType.IP : ProtocolType.Tcp;
            return new Socket(endPoint.AddressFamily, SocketType.Stream, protocolType);
        }

        /// <summary>
        /// Stop the socket listener and release accept resources.
        /// </summary>
        private void StopAndDispose() {
            try {
                _listenSocket?.Close();
                _listenSocket?.Dispose();
            }
            catch { }
            finally {
                _listenSocket = null;
            }

            // dispose acceptor event args
            foreach (SocketAsyncEventArgs e in _acceptorEventArgs) {
                try { e.Completed -= OnAcceptSocketCompleted; }
                catch { }
                try { e.Dispose(); }
                catch { }
            }
            _acceptorEventArgs.Clear();

            // release other ressources
            _server = null;
            _bufferPool = null;
        }

        /// <summary>
        /// Start accept a new client connection.
        /// </summary>
        /// <param name="e"></param>
        private void AcceptSocket(SocketAsyncEventArgs e) {
            while (CanAcceptMore()) {
                // socket must be cleared since the context object is being reused
                e.AcceptSocket = null;

                bool pending;
                try {
                    // async accept a new client connection
                    pending = _listenSocket!.AcceptAsync(e);
                }
                catch (ObjectDisposedException) {
                    return;
                }

                if (pending) {
                    return;
                }

                if (!ProcessAcceptSocket(e)) {
                    return;
                }
            }
        }

        /// <summary>
        /// Process accepted client connection.
        /// </summary>
        /// <param name="e"></param>
        /// <returns>True when the accept loop can continue.</returns>
        private bool ProcessAcceptSocket(SocketAsyncEventArgs e) {
            if (e.SocketError == SocketError.Success && e.AcceptSocket != null) {
                if (_server == null || _bufferPool == null || _server.IsStopping) {
                    try { e.AcceptSocket.Dispose(); }
                    catch { }
                    return CanAcceptMore();
                }
                // handle connection (prepare transport, then create a HttpSession)
                _ = CreateSocketSessionAsync(e.AcceptSocket, _server, _bufferPool);
            }
            else if (!(e.SocketError == SocketError.ConnectionAborted
                    || e.SocketError == SocketError.ConnectionRefused
                    || e.SocketError == SocketError.ConnectionReset
                    || e.SocketError == SocketError.OperationAborted
                    || e.SocketError == SocketError.Shutdown)
            ) {
                // ignore non fatal accept errors
            }

            // accept new client (except if socket is closed)
            return CanAcceptMore();
        }

        /// <summary>
        /// Prepare an accepted socket according to this engine configuration, then pass it to SimpleW.
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="server"></param>
        /// <param name="bufferPool"></param>
        /// <returns></returns>
        private async Task CreateSocketSessionAsync(Socket socket, SimpleWServer server, ArrayPool<byte> bufferPool) {
            ISimpleWEngine? transport = null;
            try {
                SocketTransport socketTransport = new(socket, _options, bufferPool);
                transport = socketTransport;

                if (_options.SslContext != null) {
                    transport = await socketTransport.CreateTlsTransportAsync(_options.SslContext).ConfigureAwait(false);
                }

                await server.CreateSessionAsync(transport).ConfigureAwait(false);
            }
            catch (Exception ex) {
                _log.Warn("session error", ex);
                try { transport?.Dispose(); }
                catch { }
            }
        }

        /// <summary>
        /// Callback invoked when an accept operation completes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAcceptSocketCompleted(object? sender, SocketAsyncEventArgs e) {
            if (!CanAcceptMore()) {
                return;
            }

            if (ProcessAcceptSocket(e)) {
                AcceptSocket(e);
            }
        }

        /// <summary>
        /// Indicates whether the listener can accept another socket.
        /// </summary>
        /// <returns></returns>
        private bool CanAcceptMore() {
            return _listenSocket != null
                   && !_listenSocket.SafeHandle.IsInvalid
                   && _server != null
                   && !_server.IsStopping;
        }

        #region ssl

        /// <summary>
        /// Gets whether this engine already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted => _options.SslContext != null;

        /// <summary>
        /// Configure TLS for newly accepted sockets.
        /// </summary>
        /// <param name="sslContext"></param>
        /// <returns></returns>
        public SimpleWEngine UseHttps(SslContext sslContext) {
            ArgumentNullException.ThrowIfNull(sslContext);
            if (_listenSocket != null) {
                InvalidOperationException ex = new("TLS must be configured while the engine listener is stopped.");
                _log.Warn(ex.Message, ex);
                throw ex;
            }
            _options.SslContext = sslContext;
            return this;
        }

        /// <summary>
        /// Disable TLS for newly accepted sockets.
        /// </summary>
        /// <returns></returns>
        public SimpleWEngine DisableHttps() {
            if (_listenSocket != null) {
                InvalidOperationException ex = new("TLS must be configured while the engine listener is stopped.");
                _log.Warn(ex.Message, ex);
                throw ex;
            }
            _options.SslContext = null;
            return this;
        }

        #endregion ssl

        /// <summary>
        /// Releases listener resources.
        /// </summary>
        public void Dispose() {
            StopAndDispose();
        }

    }

    /// <summary>
    /// Options for the default socket-based SimpleW engine.
    /// </summary>
    public sealed class SimpleWEngineOptions {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<SimpleWEngineOptions>();

        /// <summary>
        /// This option will set the maximum length of the pending connections queue.
        /// </summary>
        public int ListenBacklog { get; set; } = 8192;

        /// <summary>
        /// Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
        /// Will work only if socket is bound on IPv6 address.
        /// </summary>
        public bool DualMode { get; set; }

        /// <summary>
        /// This option will enable/disable Nagle's algorithm for TCP protocol
        /// </summary>
        public bool TcpNoDelay { get; set; }

        /// <summary>
        /// This option will enable/disable SO_REUSEADDR if the OS support this feature
        /// </summary>
        public bool ReuseAddress { get; set; }

        /// <summary>
        /// This option will enable/disable SO_EXCLUSIVEADDRUSE if the OS support this feature
        /// </summary>
        public bool ExclusiveAddressUse { get; set; }

        /// <summary>
        /// This option will enable SO_REUSEPORT if the OS support this feature (linux only)
        /// </summary>
        public bool ReusePort { get; set; }

        /// <summary>
        /// This option will run the accept socket on each machine's core
        /// </summary>
        public bool AcceptPerCore { get; set; }

        /// <summary>
        /// This option will setup SO_KEEPALIVE if the OS support this feature
        /// </summary>
        public bool TcpKeepAlive { get; set; }

        /// <summary>
        /// The number of seconds a TCP connection will remain alive/idle before keepalive probes are sent to the remote
        /// </summary>
        public int TcpKeepAliveTime { get; set; } = -1;

        /// <summary>
        /// The number of seconds a TCP connection will wait for a keepalive response before sending another keepalive probe
        /// </summary>
        public int TcpKeepAliveInterval { get; set; } = -1;

        /// <summary>
        /// The number of TCP keep alive probes that will be sent before the connection is terminated
        /// </summary>
        public int TcpKeepAliveRetryCount { get; set; } = -1;

        /// <summary>
        /// Option: receive buffer size
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 16 * 1024;

        /// <summary>
        /// Optional TLS configuration for the default socket engine.
        /// When set, accepted sockets are wrapped in SslStream before SimpleW creates the HttpSession.
        /// </summary>
        public SslContext? SslContext { get; set; }

        /// <summary>
        /// Check Properties and return
        /// </summary>
        /// <returns></returns>
        public SimpleWEngineOptions ValidateAndNormalize() {
            if (ListenBacklog <= 0) {
                ArgumentOutOfRangeException ex = new(nameof(ListenBacklog), "Must be > 0.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (ReceiveBufferSize <= 0) {
                ArgumentOutOfRangeException ex = new(nameof(ReceiveBufferSize), "Must be > 0.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (ReuseAddress && ExclusiveAddressUse) {
                ArgumentException ex = new($"{nameof(ReuseAddress)} and {nameof(ExclusiveAddressUse)} are mutually exclusive.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (ReusePort) {
                if (!OperatingSystem.IsLinux()) {
                    PlatformNotSupportedException ex = new($"{nameof(ReusePort)} is only supported on Linux.");
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
                if (!AcceptPerCore) {
                    ArgumentException ex = new($"{nameof(ReusePort)} is only useful on Linux when {nameof(AcceptPerCore)} is enabled.");
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
            }

            return this;
        }

    }

    /// <summary>
    /// Socket-backed transport used by the default engine.
    /// </summary>
    internal sealed class SocketTransport : ISimpleWEngine, ISimpleWTransportOutput {

        private readonly Socket _socket;
        private readonly ArrayPool<byte> _bufferPool;
        private readonly BufferedTransportInput _input;
        private NetworkStream? _stream;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint => _socket.LocalEndPoint;

        /// <summary>
        /// Remote endpoint when this transport owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

        /// <summary>
        /// Gets whether this transport already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted => false;

        /// <summary>
        /// Transport input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input => _input;

        /// <summary>
        /// Not use, only to stick with interface
        /// </summary>
        public ISimpleWTransportOutput Output => this;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="options"></param>
        /// <param name="bufferPool"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public SocketTransport(Socket socket, SimpleWEngineOptions options, ArrayPool<byte> bufferPool) {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
            ApplySocketOptions(options);
            _input = new BufferedTransportInput(ReadSocketAsync, options.ReceiveBufferSize, _bufferPool);
        }

        private Stream GetStream() => _stream ??= new NetworkStream(_socket, ownsSocket: false);

        /// <summary>
        /// ApplySocketOptions to Current Connection
        /// </summary>
        /// <param name="options"></param>
        private void ApplySocketOptions(SimpleWEngineOptions options) {
            ArgumentNullException.ThrowIfNull(options);

            _socket.LingerState = new LingerOption(enable: false, seconds: 0);

            if (options.TcpKeepAlive) {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            if (options.TcpKeepAliveTime >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, options.TcpKeepAliveTime);
            }
            if (options.TcpKeepAliveInterval >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, options.TcpKeepAliveInterval);
            }
            if (options.TcpKeepAliveRetryCount >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, options.TcpKeepAliveRetryCount);
            }
            if (options.TcpNoDelay) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            }
        }

        /// <summary>
        /// Check if connection is closed
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        private static bool IsConnectionClosed(SocketError error) {
            return error == SocketError.ConnectionReset
                || error == SocketError.ConnectionAborted
                || error == SocketError.Shutdown
                || error == SocketError.OperationAborted;
        }

        #region ISimpleWTransportInput

        /// <summary>
        /// Read bytes from the transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="IOException"></exception>
        private async ValueTask<int> ReadSocketAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
            try {
                return await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException se) when (IsConnectionClosed(se.SocketErrorCode)) {
                return 0;
            }
            catch (SocketException se) {
                throw new IOException("Unable to read from the socket transport.", se);
            }
        }

        #endregion ISimpleWTransportInput

        #region ISimpleWTransportOutput

        /// <summary>
        /// Storage Segment Array for SendAsync(header, body)
        /// </summary>
        private readonly ArraySegment<byte>[] _sendSegments2 = new ArraySegment<byte>[2];

        /// <summary>
        /// State gate for output writes.
        /// </summary>
        private int _writing;

        /// <summary>
        /// Writes all bytes to the transport (thread safe).
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="IOException"></exception>
        public async ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                int sent = 0;
                while (sent < buffer.Length) {
                    int bytesSent = await _socket.SendAsync(buffer.Slice(sent), SocketFlags.None, cancellationToken).ConfigureAwait(false);
                    if (bytesSent <= 0) {
                        throw new IOException("Unable to write to the socket transport.");
                    }
                    sent += bytesSent;
                }
                return sent;
            }
            catch (SocketException se) {
                throw new IOException("Unable to write to the socket transport.", se);
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes all provided segments to the transport (thread safe).
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="IOException"></exception>
        public async ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                int sent = await _socket.SendAsync(segments, SocketFlags.None).ConfigureAwait(false); 
                return sent;
            }
            catch (SocketException se) {
                throw new IOException("Unable to write to the socket transport.", se);
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes both segments to the transport (thread safe).
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="IOException"></exception>
        public async ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                _sendSegments2[0] = header;
                _sendSegments2[1] = body;
                int sent = await _socket.SendAsync(_sendSegments2, SocketFlags.None).ConfigureAwait(false);
                return sent;
            }
            catch (SocketException se) {
                throw new IOException("Unable to write to the socket transport.", se);
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Acquire lock and throw if already
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent WriteAsync on same transport output");
            }
        }

        /// <summary>
        /// Release lock
        /// </summary>
        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        #endregion ISimpleWTransportOutput

        #region process

        /// <summary>
        /// Tries to check whether the remote peer is already closed.
        /// Returns false when the engine has no cheap probing capability.
        /// </summary>
        /// <param name="isClosed"></param>
        /// <returns></returns>
        public bool TryCheckConnectionClosed(out bool isClosed) {
            try {
                isClosed = _socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0;
                return true;
            }
            catch {
                isClosed = true;
                return true;
            }
        }

        /// <summary>
        /// Closes the accepted connection gracefully.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Aborts the accepted connection. When reset is true, implementations should use an abortive close if supported.
        /// </summary>
        /// <param name="reset"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            if (reset) {
                try { _socket.LingerState = new LingerOption(enable: true, seconds: 0); }
                catch { }
            }
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// GetFeature
        /// </summary>
        /// <typeparam name="TFeature"></typeparam>
        /// <returns></returns>
        public TFeature? GetFeature<TFeature>() where TFeature : class => null;

        #endregion process

        #region ssl

        /// <summary>
        /// Wrap this socket transport in an SslStream before the HttpSession is created.
        /// </summary>
        /// <param name="sslContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<ISimpleWEngine> CreateTlsTransportAsync(SslContext sslContext, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(sslContext);

            // SslStream above NetworkStream
            SslStream sslStream = new(
                innerStream: GetStream(),
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: sslContext.ClientCertificateValidation
            );

            try {
                // server side TLS Handshake
                await sslStream.AuthenticateAsServerAsync(
                    serverCertificate: sslContext.Certificate,
                    clientCertificateRequired: sslContext.ClientCertificateRequired,
                    enabledSslProtocols: sslContext.Protocols,
                    checkCertificateRevocation: sslContext.CheckCertificateRevocation
                ).ConfigureAwait(false);
            }
            catch {
                try { sslStream.Dispose(); }
                catch { }
                throw;
            }

            // the sslStream is encapsulated in a StreamTransport
            return new StreamTransport(
                sslStream,
                _bufferPool,
                LocalEndPoint,
                RemoteEndPoint,
                isEncrypted: true,
                owner: this,
                receiveBufferSize: _input.BufferSize
            );
        }

        #endregion ssl

        #region dispose

        private bool _disposed;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;

            try {
                if (_socket.Connected) {
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch { }

            try { _input.Dispose(); }
            catch { }
            try { _stream?.Dispose(); }
            catch { }
            try { _socket.Dispose(); }
            catch { }
        }

        #endregion dispose

    }

    /// <summary>
    /// Stream-backed transport, used mainly after TLS wrapping.
    /// </summary>
    internal sealed class StreamTransport : ISimpleWEngine, ISimpleWTransportOutput, ISimpleWTransportTlsFeature {

        private readonly Stream _stream;
        private readonly ISimpleWEngine? _owner;
        private readonly BufferedTransportInput _input;
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint { get; }

        /// <summary>
        /// Remote endpoint when this transport owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint { get; }

        /// <summary>
        /// Gets whether this transport already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted { get; }

        /// <summary>
        /// Transport input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input => _input;

        /// <summary>
        /// Not use, only to stick with interface
        /// </summary>
        public ISimpleWTransportOutput Output => this;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="bufferPool"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="isEncrypted"></param>
        /// <param name="owner"></param>
        /// <param name="receiveBufferSize"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public StreamTransport(
            Stream stream,
            ArrayPool<byte> bufferPool,
            EndPoint? localEndPoint = null,
            EndPoint? remoteEndPoint = null,
            bool isEncrypted = false,
            ISimpleWEngine? owner = null,
            int receiveBufferSize = 16 * 1024
        ) {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
            IsEncrypted = isEncrypted;
            _owner = owner;
            _input = new BufferedTransportInput(ReadStreamAsync, receiveBufferSize, _bufferPool);
        }

        #region ssl

        /// <summary>
        /// Gets the client certificate when available.
        /// </summary>
        public X509Certificate2? ClientCertificate {
            get {
                if (_stream is not SslStream sslStream) {
                    return null;
                }
                if (sslStream.RemoteCertificate is X509Certificate2 x509) {
                    return x509;
                }
                if (sslStream.RemoteCertificate is X509Certificate cert) {
                    return new X509Certificate2(cert);
                }
                return null;
            }
        }

        #endregion ssl

        #region ISimpleWTransportInput

        /// <summary>
        /// Read bytes from the transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private ValueTask<int> ReadStreamAsync(Memory<byte> buffer, CancellationToken cancellationToken) => _stream.ReadAsync(buffer, cancellationToken);

        #endregion ISimpleWTransportInput

        #region ISimpleWTransportOutput

        /// <summary>
        /// Storage Segment Array for SendAsync(header, body)
        /// </summary>
        private readonly ArraySegment<byte>[] _sendSegments2 = new ArraySegment<byte>[2];

        /// <summary>
        /// State gate for output writes.
        /// </summary>
        private int _writing;

        /// <summary>
        /// Writes all bytes to the transport (thread safe).
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                return buffer.Length;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes all provided segments to the transport (thread safe).
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                return await WriteSegmentsAsync(segments, cancellationToken).ConfigureAwait(false);
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes both segments to the transport (thread safe).
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                _sendSegments2[0] = header;
                _sendSegments2[1] = body;
                return await WriteSegmentsAsync(_sendSegments2, cancellationToken).ConfigureAwait(false);
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Acquire lock and throw if already
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent WriteAsync on same transport output");
            }
        }

        /// <summary>
        /// Release lock
        /// </summary>
        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        /// <summary>
        /// WriteSegmentsAsync
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async ValueTask<long> WriteSegmentsAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken) {

            if (IsEncrypted) {
                return await _SendTlsSegmentsAsync(segments, cancellationToken).ConfigureAwait(false);
            }

            long bytesWritten = 0;
            foreach (ArraySegment<byte> segment in segments) {
                if (segment.Array == null || segment.Count <= 0) {
                    continue;
                }
                await _stream.WriteAsync(segment.AsMemory(), cancellationToken).ConfigureAwait(false);
                bytesWritten += segment.Count;
            }
            return bytesWritten;
        }

        #region send tls optimization

        /// <summary>
        /// Coalesce small TLS scatter/gather writes into one SslStream write; larger payloads avoid an extra copy.
        /// </summary>
        private const int TlsCoalesceThresholdBytes = 64 * 1024;

        private async ValueTask<long> _SendTlsSegmentsAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken) {
            int total = 0;
            foreach (ArraySegment<byte> seg in segments) {
                if (seg.Array == null || seg.Count <= 0) {
                    continue;
                }
                if (seg.Count > TlsCoalesceThresholdBytes - total) {
                    return await _SendTlsSegmentsIndividuallyAsync(segments, cancellationToken).ConfigureAwait(false);
                }
                total += seg.Count;
            }

            if (total == 0) {
                return total;
            }

            byte[] buffer = _bufferPool.Rent(total);
            try {
                int offset = 0;
                foreach (ArraySegment<byte> seg in segments) {
                    if (seg.Array == null || seg.Count <= 0) {
                        continue;
                    }
                    Buffer.BlockCopy(seg.Array, seg.Offset, buffer, offset, seg.Count);
                    offset += seg.Count;
                }

                await _stream.WriteAsync(buffer.AsMemory(0, total), cancellationToken).ConfigureAwait(false);
                return total;
            }
            finally {
                _bufferPool.Return(buffer);
            }
        }

        private async ValueTask<long> _SendTlsSegmentsIndividuallyAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken) {
            long total = 0;
            foreach (ArraySegment<byte> seg in segments) {
                if (seg.Array == null || seg.Count <= 0) {
                    continue;
                }
                await _stream.WriteAsync(seg.AsMemory(), cancellationToken).ConfigureAwait(false);
                total += seg.Count;
            }
            return total;
        }

        #endregion send tls optimization

        #endregion ISimpleWTransportOutput

        #region process

        /// <summary>
        /// Tries to check whether the remote peer is already closed.
        /// Returns false when the engine has no cheap probing capability.
        /// </summary>
        /// <param name="isClosed"></param>
        /// <returns></returns>
        public bool TryCheckConnectionClosed(out bool isClosed) {
            if (_owner != null) {
                return _owner.TryCheckConnectionClosed(out isClosed);
            }
            isClosed = false;
            return false;
        }

        /// <summary>
        /// Closes the accepted connection gracefully.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Aborts the accepted connection. When reset is true, implementations should use an abortive close if supported.
        /// </summary>
        /// <param name="reset"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            try { _stream.Dispose(); }
            catch { }
            if (_owner != null) {
                await _owner.AbortAsync(reset, cancellationToken).ConfigureAwait(false);
            }
            else {
                Dispose();
            }
        }

        /// <summary>
        /// Gets an optional feature exposed by this engine.
        /// </summary>
        /// <typeparam name="TFeature"></typeparam>
        /// <returns></returns>
        public TFeature? GetFeature<TFeature>() where TFeature : class {
            if (typeof(TFeature) == typeof(ISimpleWTransportTlsFeature)) {
                return this as TFeature;
            }
            return _owner?.GetFeature<TFeature>();
        }

        #endregion process

        #region dispose

        private bool _disposed;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;

            try { _input.Dispose(); }
            catch { }
            try { _stream.Dispose(); }
            catch { }
            try { _owner?.Dispose(); }
            catch { }
        }

        #endregion dispose

    }

    /// <summary>
    /// ArrayPool-backed input for transports that expose read-into-buffer APIs.
    /// </summary>
    internal sealed class BufferedTransportInput : ISimpleWTransportInput, IDisposable {

        private readonly Func<Memory<byte>, CancellationToken, ValueTask<int>> _readAsync;
        private readonly ArrayPool<byte> _bufferPool;
        private byte[] _buffer;
        private int _start;
        private int _end;
        private bool _isCompleted;
        private bool _needMoreData;

        public int BufferSize { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="readAsync"></param>
        /// <param name="receiveBufferSize"></param>
        /// <param name="bufferPool"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public BufferedTransportInput(Func<Memory<byte>, CancellationToken, ValueTask<int>> readAsync, int receiveBufferSize, ArrayPool<byte> bufferPool) {
            _readAsync = readAsync ?? throw new ArgumentNullException(nameof(readAsync));
            _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
            BufferSize = Math.Max(1, receiveBufferSize);
            _buffer = _bufferPool.Rent(BufferSize);
        }

        /// <summary>
        /// Reads or returns currently buffered bytes.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
            if (_start < _end && !_needMoreData) {
                return CurrentResult();
            }

            if (!_isCompleted) {
                EnsureWritableSpace(BufferSize);
                int read = await _readAsync(_buffer.AsMemory(_end, _buffer.Length - _end), cancellationToken).ConfigureAwait(false);
                _needMoreData = false;

                if (read <= 0) {
                    _isCompleted = true;
                }
                else {
                    _end += read;
                }
            }

            return CurrentResult();
        }

        /// <summary>
        /// Return Current Buffer
        /// </summary>
        /// <returns></returns>
        private SimpleWTransportReadResult CurrentResult() {
            ReadOnlySequence<byte> sequence = _start < _end
                                                ? new ReadOnlySequence<byte>(_buffer, _start, _end - _start)
                                                : ReadOnlySequence<byte>.Empty;

            return new SimpleWTransportReadResult(sequence, _isCompleted);
        }

        /// <summary>
        /// Advances the input by the number of consumed and examined bytes from the last buffer.
        /// </summary>
        /// <param name="consumedBytes"></param>
        /// <param name="examinedBytes"></param>
        public void AdvanceTo(long consumedBytes, long examinedBytes) {
            int available = _end - _start;
            int consumed = checked((int)Math.Min(Math.Max(consumedBytes, 0), available));
            int examined = checked((int)Math.Min(Math.Max(examinedBytes, 0), available));

            _start += consumed;
            if (_start == _end) {
                _start = 0;
                _end = 0;
                _needMoreData = false;
                return;
            }

            _needMoreData = consumed == 0 && examined >= available;
        }

        /// <summary>
        /// Enlarge Buffer if needed
        /// </summary>
        /// <param name="minimumFreeBytes"></param>
        private void EnsureWritableSpace(int minimumFreeBytes) {
            if (_buffer.Length - _end >= minimumFreeBytes) {
                return;
            }

            if (_start > 0) {
                int remaining = _end - _start;
                if (remaining > 0) {
                    Buffer.BlockCopy(_buffer, _start, _buffer, 0, remaining);
                }
                _start = 0;
                _end = remaining;

                if (_buffer.Length - _end >= minimumFreeBytes) {
                    return;
                }
            }

            int required = _end + minimumFreeBytes;
            int newSize = _buffer.Length * 2;
            if (newSize < required) {
                newSize = required;
            }

            byte[] newBuffer = _bufferPool.Rent(newSize);
            if (_end > 0) {
                Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _end);
            }
            _bufferPool.Return(_buffer);
            _buffer = newBuffer;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            byte[] buffer = _buffer;
            if (buffer.Length == 0) {
                return;
            }
            _buffer = Array.Empty<byte>();
            _start = 0;
            _end = 0;
            _bufferPool.Return(buffer);
        }

    }

    /// <summary>
    /// Delegate-backed transport.
    /// Useful for custom transports and tests that do not want to create a concrete transport type.
    /// </summary>
    public sealed class DelegateTransport : ISimpleWEngine {

        private readonly Func<CancellationToken, ValueTask>? _closeAsync;
        private readonly Func<bool, CancellationToken, ValueTask>? _abortAsync;
        private readonly Func<Type, object?>? _getFeature;
        private readonly Func<(bool supported, bool isClosed)>? _checkConnectionClosed;
        private readonly Action? _dispose;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint { get; }

        /// <summary>
        /// Remote endpoint when this transport owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint { get; }

        /// <summary>
        /// Gets whether this transport already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted { get; }

        /// <summary>
        /// Transport input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input { get; }

        /// <summary>
        /// Transport output for an accepted connection.
        /// </summary>
        public ISimpleWTransportOutput Output { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="readAsync"></param>
        /// <param name="writeAsync"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="isEncrypted"></param>
        /// <param name="closeAsync"></param>
        /// <param name="abortAsync"></param>
        /// <param name="getFeature"></param>
        /// <param name="checkConnectionClosed"></param>
        /// <param name="dispose"></param>
        /// <param name="receiveBufferSize"></param>
        /// <param name="bufferPool"></param>
        public DelegateTransport(
            Func<Memory<byte>, CancellationToken, ValueTask<int>> readAsync,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<long>> writeAsync,
            EndPoint? localEndPoint = null,
            EndPoint? remoteEndPoint = null,
            bool isEncrypted = false,
            Func<CancellationToken, ValueTask>? closeAsync = null,
            Func<bool, CancellationToken, ValueTask>? abortAsync = null,
            Func<Type, object?>? getFeature = null,
            Func<(bool supported, bool isClosed)>? checkConnectionClosed = null,
            Action? dispose = null,
            int receiveBufferSize = 16 * 1024,
            ArrayPool<byte>? bufferPool = null
        ) {
            ArgumentNullException.ThrowIfNull(readAsync);
            ArgumentNullException.ThrowIfNull(writeAsync);
            _closeAsync = closeAsync;
            _abortAsync = abortAsync;
            _getFeature = getFeature;
            _checkConnectionClosed = checkConnectionClosed;
            _dispose = dispose;
            LocalEndPoint = localEndPoint;
            RemoteEndPoint = remoteEndPoint;
            IsEncrypted = isEncrypted;
            Input = new BufferedTransportInput(readAsync, receiveBufferSize, bufferPool ?? ArrayPool<byte>.Shared);
            Output = new DelegateTransportOutput(writeAsync);
        }

        #region process

        /// <summary>
        /// Tries to check whether the remote peer is already closed.
        /// Returns false when the engine has no cheap probing capability.
        /// </summary>
        /// <param name="isClosed"></param>
        /// <returns></returns>
        public bool TryCheckConnectionClosed(out bool isClosed) {
            if (_checkConnectionClosed == null) {
                isClosed = false;
                return false;
            }
            (bool supported, bool closed) = _checkConnectionClosed();
            isClosed = closed;
            return supported;
        }

        /// <summary>
        /// Closes the accepted connection gracefully.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
            => _closeAsync?.Invoke(cancellationToken) ?? ValueTask.CompletedTask;

        /// <summary>
        /// Aborts the accepted connection. When reset is true, implementations should use an abortive close if supported.
        /// </summary>
        /// <param name="reset"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default)
            => _abortAsync?.Invoke(reset, cancellationToken) ?? CloseAsync(cancellationToken);

        /// <summary>
        /// Gets an optional feature exposed by this engine.
        /// </summary>
        /// <typeparam name="TFeature"></typeparam>
        /// <returns></returns>
        public TFeature? GetFeature<TFeature>() where TFeature : class => _getFeature?.Invoke(typeof(TFeature)) as TFeature;

        #endregion process

        /// <summary>
        /// Releases engine resources.
        /// </summary>
        public void Dispose() {
            try { (Input as IDisposable)?.Dispose(); }
            catch { }
            try { _dispose?.Invoke(); }
            catch { }
        }

    }

    /// <summary>
    /// DelegateTransportOutput
    /// </summary>
    public sealed class DelegateTransportOutput : ISimpleWTransportOutput {

        private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<long>> _writeAsync;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="writeAsync"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public DelegateTransportOutput(Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask<long>> writeAsync) {
            _writeAsync = writeAsync ?? throw new ArgumentNullException(nameof(writeAsync));
        }

        #region ISimpleWTransportOutput

        /// <summary>
        /// State gate for output writes.
        /// </summary>
        private int _writing;

        /// <summary>
        /// Writes all bytes to the transport (thread safe).
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                return await _writeAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes all provided segments to the transport (thread safe).
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                long bytesWritten = 0;
                if (header.Count > 0) {
                    bytesWritten += await _writeAsync(header, cancellationToken).ConfigureAwait(false);
                }
                if (body.Count > 0) {
                    bytesWritten += await _writeAsync(body, cancellationToken).ConfigureAwait(false);
                }
                return bytesWritten;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes both segments to the transport (thread safe).
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                ArgumentNullException.ThrowIfNull(segments);
                long bytesWritten = 0;
                foreach (ArraySegment<byte> segment in segments) {
                    if (segment.Array == null || segment.Count <= 0) {
                        continue;
                    }
                    bytesWritten += await _writeAsync(segment.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                return bytesWritten;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Acquire lock and throw if already
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent WriteAsync on same transport output");
            }
        }

        /// <summary>
        /// Release lock
        /// </summary>
        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        #endregion ISimpleWTransportOutput

    }

}
