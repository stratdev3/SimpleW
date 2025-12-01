using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;


namespace RawServer {

    /// <summary>
    /// RawServer
    /// </summary>
    public class RawServer {

        #region constructor

        /// <summary>
        /// Initialize TCP server with a given IP address and port number
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        public RawServer(IPAddress address, int port) : this(new IPEndPoint(address, port)) { }

        /// <summary>
        /// Initialize TCP server with a given IP endpoint
        /// </summary>
        /// <param name="endpoint">IP endpoint</param>
        public RawServer(EndPoint endpoint) {
            _endPoint = endpoint;
        }

        #endregion constructor

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
        /// Lifetime CTS (interne au serveur)
        /// </summary>
        private CancellationTokenSource? _lifetimeCts;

        /// <summary>
        /// Task qui représente la boucle de vie du serveur
        /// </summary>
        private Task? _runTask;

        /// <summary>
        /// Start the server
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>'true' if the server was successfully started, 'false' if the server failed to start</returns>
        public virtual Task StartAsync(CancellationToken cancellationToken = default) {

            if (IsStarted) {
                return _runTask ?? Task.CompletedTask;
            }

            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsStarted = true;
            IsStopping = false;

            ListenSocket();

            // keep thread until cancelled
            _runTask = WaitForCancellationAsync(_lifetimeCts.Token);
            return _runTask;
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

            if (_runTask != null) {
                try {
                    await _runTask;
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

                // reset state
                IsStarted = false;
                IsStopping = false;

                _lifetimeCts?.Dispose();
                _lifetimeCts = null;
                _runTask = null;
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

        /// <summary>
        /// Idle timeout (if no data received during timeout, then close connection)
        /// Set TimeSpan.MinValue to disable.
        /// </summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

        #endregion security

        #region network

        /// <summary>
        /// BufferPool
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Listen Endpoint
        /// </summary>
        public EndPoint _endPoint { get; private set; }

        /// <summary>
        /// Listen Address
        /// </summary>
        public IPAddress Address => ((IPEndPoint)_endPoint).Address;

        /// <summary>
        /// Listen Port
        /// </summary>
        public int Port => ((IPEndPoint)_endPoint).Port;

        /// <summary>
        /// Liste Socket
        /// </summary>
        private Socket? _listenSocket;

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

        #endregion options

        /// <summary>
        /// Create Socket
        /// </summary>
        /// <returns>Socket object</returns>
        protected virtual Socket CreateSocket() {
            ProtocolType protocolType = _endPoint is UnixDomainSocketEndPoint ? ProtocolType.IP : ProtocolType.Tcp;
            return new Socket(_endPoint.AddressFamily, SocketType.Stream, protocolType);
        }

        /// <summary>
        /// Listen to Socket
        /// </summary>
        private void ListenSocket() {

            // create socket
            _listenSocket = CreateSocket();

            // option: reuse address
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, OptionReuseAddress);
            // option: exclusive address use
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, OptionExclusiveAddressUse);
            // Apply the option: dual mode (this option must be applied before listening)
            if (_endPoint.AddressFamily == AddressFamily.InterNetworkV6) {
                _listenSocket.DualMode = OptionDualMode;
            }

            // bind socket to endpoint
            _listenSocket.Bind(_endPoint);
            // refresh the endpoint property based on the actual endpoint created
            //_endPoint = _listenSocket.LocalEndPoint; // todo fix this warning
            // start listen to the socket with the given accepting backlog size
            _listenSocket.Listen(OptionListenSocketBacklog);

            for (int i = 0; i < Math.Max(Environment.ProcessorCount / 2, 2); i++) {

                // SocketAsyncEventArgs
                SocketAsyncEventArgs listenSocketEventArgs = new();
                listenSocketEventArgs.Completed += OnAcceptSocketCompleted;

                // On lance la première acceptation
                AcceptSocket(listenSocketEventArgs);
            }

            // update the started flag
            IsStarted = true;
        }

        /// <summary>
        /// Start accept a new client connection
        /// </summary>
        /// <param name="e"></param>
        private void AcceptSocket(SocketAsyncEventArgs e) {
            if (_listenSocket == null) {
                return;
            }

            // socket must be cleared since the context object is being reused
            e.AcceptSocket = null;

            // async accept a new client connection
            try {
                if (!_listenSocket.AcceptAsync(e)) {
                    ProcessAcceptSocket(e);
                }
            }
            catch (ObjectDisposedException) {
                return;
            }
        }

        /// <summary>
        /// Process accepted client connection
        /// </summary>
        private void ProcessAcceptSocket(SocketAsyncEventArgs e) {
            if (e.SocketError == SocketError.Success && e.AcceptSocket != null) {
                HttpContext context = new(this, e.AcceptSocket, _bufferPool);
                context.Connect();
            }
            else {
                // skip disconnect errors
                if (e.SocketError == SocketError.ConnectionAborted
                    || e.SocketError == SocketError.ConnectionRefused
                    || e.SocketError == SocketError.ConnectionReset
                    || e.SocketError == SocketError.OperationAborted
                    || e.SocketError == SocketError.Shutdown
                ) {
                    Console.WriteLine($"[HTTP]disconnect");
                    return;
                }
                Console.WriteLine($"[HTTP] Connection error: e.SocketError {e.SocketError}");
                OnError(e.SocketError);
            }

            // accept new client (except if socket is closed)
            if (_listenSocket != null && !_listenSocket.SafeHandle.IsInvalid) {
                AcceptSocket(e);
            }
        }

        /// <summary>
        /// This method is the callback method associated with Socket.AcceptAsync()
        /// operations and is invoked when an accept operation is complete
        /// </summary>
        private void OnAcceptSocketCompleted(object? sender, SocketAsyncEventArgs e) {
            ProcessAcceptSocket(e);
        }

        #endregion session

    }


    /// <summary>
    /// HttpContext
    /// </summary>
    public class HttpContext {

        private readonly RawServer Server;

        private readonly Socket Socket;
        private readonly ArrayPool<byte> _bufferPool;

        public int OptionReceiveBufferSize { get; set; } = 8192;
        public int OptionSendBufferSize { get; set; } = 8192;

        private byte[] _receiveBuffer;
        private SocketAsyncEventArgs _receiveEventArg;


        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="server"></param>
        public HttpContext(RawServer server, Socket socket, ArrayPool<byte> bufferPool) {
            Server = server;
            Socket = socket;
            _bufferPool = bufferPool;

            // Setup buffers
            _receiveBuffer = _bufferPool.Rent(OptionReceiveBufferSize);

            // Setup event args
            _receiveEventArg = new SocketAsyncEventArgs();
            _receiveEventArg.Completed += OnAsyncCompleted;

            SetCacheResponse();
        }

        /// <summary>
        /// Connect
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="buffer"></param>
        internal void Connect() {

            // Apply the option: keep alive
            if (Server.OptionKeepAlive)
                Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            if (Server.OptionTcpKeepAliveTime >= 0)
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, Server.OptionTcpKeepAliveTime);
            if (Server.OptionTcpKeepAliveInterval >= 0)
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Server.OptionTcpKeepAliveInterval);
            if (Server.OptionTcpKeepAliveRetryCount >= 0)
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Server.OptionTcpKeepAliveRetryCount);
            // Apply the option: no delay
            if (Server.OptionNoDelay)
                Socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

            TryReceive();
        }

        /// <summary>
        /// TryReceive
        /// </summary>
        private void TryReceive() {
            bool process = true;
            while (process) {
                process = false;
                try {
                    //_receiveEventArg.SetBuffer(_receiveBuffer, 0, (int)_receiveBuffer.Length);
                    if (_receiveEventArg.Buffer == null) {
                        _receiveEventArg.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                    }

                    if (!Socket.ReceiveAsync(_receiveEventArg)) {
                        process = ProcessReceive(_receiveEventArg);
                    }
                }
                catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// ProcessReceive
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        private bool ProcessReceive(SocketAsyncEventArgs e) {

            long size = e.BytesTransferred;

            // received some data from the client
            if (size > 0) {
                _ = SendCacheAsync();
                //_ = SendCache();
            }

            // try to receive again if the session is valid
            if (e.SocketError == SocketError.Success) {
                if (size > 0) {
                    return true;
                }
                else {
                    Disconnect();
                }
            }
            else {
                //Console.WriteLine("ProcessReceive error");
                Disconnect();
            }

            return false;
        }

        /// <summary>
        /// OnAsyncCompleted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <exception cref="ArgumentException"></exception>
        private void OnAsyncCompleted(object? sender, SocketAsyncEventArgs e) {
            switch (e.LastOperation) {
                case SocketAsyncOperation.Receive:
                    if (ProcessReceive(e)) {
                        TryReceive();
                    }
                    break;
                default:
                    Console.WriteLine("The last operation completed on the socket was not a receive or send");
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        public async ValueTask SendCacheAsync() {
            if (Socket == null || !Socket.Connected) {
                return;
            }
            await Socket.SendAsync(CachedReponse, SocketFlags.None).ConfigureAwait(false);
        }

        public ValueTask<int> SendCache() {
            if (!Socket.Connected) {
                return ValueTask.FromResult(0);
            }

            return new ValueTask<int>(Socket.SendAsync(CachedReponse, SocketFlags.None));
        }

        private static byte[] CachedReponse = Array.Empty<byte>();

        private void SetCacheResponse() {
            if (CachedReponse != Array.Empty<byte>()) {
                return;
            }

            const string body = """{ "message": "Hello world !" }""";

            // On prépare une seule fois tout le message HTTP
            var headerBuilder = new StringBuilder();
            headerBuilder.Append("HTTP/1.1 200 OK\r\n");
            headerBuilder.Append("Content-Type: application/json; charset=utf-8\r\n");
            headerBuilder.Append("Content-Length: ");
            headerBuilder.Append(Encoding.UTF8.GetByteCount(body));
            headerBuilder.Append("\r\n");
            // Laisse la connexion ouverte pour permettre le keep-alive
            //headerBuilder.Append("Connection: close\r\n");
            headerBuilder.Append("\r\n");

            var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            CachedReponse = new byte[headerBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, CachedReponse, 0, headerBytes.Length);
            Buffer.BlockCopy(bodyBytes, 0, CachedReponse, headerBytes.Length, bodyBytes.Length);
        }


        /// <summary>
        /// Disconnect
        /// </summary>
        /// <returns></returns>
        private bool Disconnect() {
            _receiveEventArg.Completed -= OnAsyncCompleted;

            try {
                try {
                    Socket.Shutdown(SocketShutdown.Both);
                }
                catch (SocketException) { }
                Socket.Close();
                Socket.Dispose();
                _receiveEventArg.Dispose();

                if (_receiveBuffer != null) {
                    _bufferPool.Return(_receiveBuffer);
                    _receiveBuffer = null!;
                }

            }
            catch (ObjectDisposedException) { }

            return true;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            Disconnect();
        }

        #region hide

        /// <summary>
        /// SendAsync
        /// </summary>
        /// <param name="_socket"></param>
        /// <returns></returns>
        public async Task SendAsync(string body = """{ "message": "Hello world !" }""") {
            if (Socket == null || !Socket.Connected) {
                Console.WriteLine($"SendAsync error no Socket");
                return;
            }

            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            string headers = "HTTP/1.1 200 OK\r\n" +
                             "Content-Type: application/json; charset=utf-8\r\n" +
                             $"Content-Length: {bodyBytes.Length}\r\n" +
                             //"Connection: close\r\n" +
                             "\r\n";

            byte[] headerBytes = Encoding.UTF8.GetBytes(headers);

            var segments = new List<ArraySegment<byte>>(2) {
                new ArraySegment<byte>(headerBytes),
                new ArraySegment<byte>(bodyBytes)
            };

            try {
                await Socket.SendAsync(segments, SocketFlags.None).ConfigureAwait(false);
            }
            catch (Exception ex) {
                Console.WriteLine($"SendAsync error {ex.Message}");
            }
        }

        #endregion hide

    }

}
