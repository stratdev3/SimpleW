using System.Net;
using System.Net.Sockets;


namespace SimpleW {

    /// <summary>
    /// Default socket-based network engine used by SimpleW.
    /// One engine instance manages one server at a time.
    /// </summary>
    public class SimpleWEngine : ISimpleWEngine {

        /// <summary>
        /// Active listen socket.
        /// </summary>
        private Socket? _listenSocket;

        /// <summary>
        /// SocketAsyncEventArgs list.
        /// </summary>
        private readonly List<SocketAsyncEventArgs> _acceptorEventArgs = new();

        /// <summary>
        /// Accepted connection handler owned by the server.
        /// </summary>
        private Func<Socket, Task>? _connectionHandler;

        /// <summary>
        /// Owning server for this engine instance.
        /// </summary>
        private SimpleWServer? _server;

        /// <summary>
        /// Engine display name.
        /// </summary>
        public string Name => nameof(SimpleWEngine);

        /// <summary>
        /// Start the engine and begin accepting connections.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="options"></param>
        /// <param name="connectionHandler"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public Task<EndPoint?> StartAsync(
            SimpleWServer server,
            SimpleWSServerOptions options,
            Func<Socket, Task> connectionHandler,
            CancellationToken cancellationToken = default
        ) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(connectionHandler);

            if (_server != null && !ReferenceEquals(_server, server)) {
                throw new InvalidOperationException("This engine instance is already attached to another SimpleWServer.");
            }
            if (_listenSocket != null) {
                throw new InvalidOperationException("The engine is already started for this server.");
            }

            _server = server;
            _connectionHandler = connectionHandler;

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
            _connectionHandler = null;
            _server = null;
        }

        /// <summary>
        /// Start accept a new client connection.
        /// </summary>
        /// <param name="e"></param>
        private void AcceptSocket(SocketAsyncEventArgs e) {
            if (_listenSocket == null || _server == null || _server.IsStopping) {
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
        /// Process accepted client connection.
        /// </summary>
        /// <param name="e"></param>
        private void ProcessAcceptSocket(SocketAsyncEventArgs e) {
            if (e.SocketError == SocketError.Success && e.AcceptSocket != null && _connectionHandler != null) {
                // handle connection (create a HttpSession)
                _ = _connectionHandler(e.AcceptSocket);
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
            if (_listenSocket != null && !_listenSocket.SafeHandle.IsInvalid && _server != null && !_server.IsStopping) {
                AcceptSocket(e);
            }
        }

        /// <summary>
        /// Callback invoked when an accept operation completes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAcceptSocketCompleted(object? sender, SocketAsyncEventArgs e) {
            if (_listenSocket == null || _server == null || _listenSocket.SafeHandle.IsInvalid || _server.IsStopping) {
                return;
            }

            ProcessAcceptSocket(e);
        }

    }

    /// <summary>
    /// Network engine used by <see cref="SimpleWServer"/> to accept connections.
    /// </summary>
    public interface ISimpleWEngine {

        /// <summary>
        /// Engine display name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Start the engine and begin accepting connections.
        /// Return the effective bound endpoint when it differs from the configured one.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="options"></param>
        /// <param name="connectionHandler"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<EndPoint?> StartAsync(
            SimpleWServer server,
            SimpleWSServerOptions options,
            Func<Socket, Task> connectionHandler,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Stop the engine and release its listener resources.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default);

    }

}
