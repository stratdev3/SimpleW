using System.Buffers;
using System.Net;
using ioxide;
using ioxide.tls;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// Minimal SimpleW listener built like the ioxide TCP playgrounds.
    /// </summary>
    public sealed class IoxideEngine : ISimpleWEngine {

        private readonly IoxideEngineOptions _options;
        private readonly Action<Reactor>? _onReactorStart;

        private Reactor[] _reactors = [];
        private Thread[] _threads = [];
        private SimpleWServer? _server;
        private IPEndPoint? _endpoint;
        private ArrayPool<byte>? _bufferPool;
        private bool _deferFlush;
        private bool _tlsEnabled;
        private int _tlsStartAttempted;
        private int _disposed;

        /// <summary>
        /// Gets the engine name reported to SimpleW.
        /// </summary>
        public string Name => nameof(IoxideEngine);

        /// <summary>
        /// Gets whether the engine is configured to terminate TLS connections.
        /// </summary>
        public bool IsEncrypted => _tlsEnabled || _options.Tls != null;

        /// <summary>
        /// Initializes an ioxide engine using the specified integration options.
        /// </summary>
        /// <param name="options">
        /// Integration options, or <see langword="null"/> to use the defaults.
        /// </param>
        /// <param name="onReactorStart">
        /// Optional callback executed on each reactor thread after its services are initialized.
        /// </param>
        public IoxideEngine(IoxideEngineOptions? options = null, Action<Reactor>? onReactorStart = null) {
            _options = options ?? new IoxideEngineOptions();
            _onReactorStart = onReactorStart;
        }

        /// <summary>
        /// Initializes an ioxide engine using a native server configuration.
        /// </summary>
        /// <param name="config">Native ioxide server configuration.</param>
        /// <param name="onReactorStart">
        /// Optional callback executed on each reactor thread after its services are initialized.
        /// </param>
        public IoxideEngine(ServerConfig config, Action<Reactor>? onReactorStart = null) : this(
            new IoxideEngineOptions {
                ServerConfig = config ?? throw new ArgumentNullException(nameof(config))
            }, onReactorStart
        ) {
        }

        /// <summary>
        /// Starts the ioxide reactors and opens the configured TCP listeners.
        /// </summary>
        /// <param name="server">SimpleW server served by this engine.</param>
        /// <param name="bufferPool">Pool used by encrypted connections to retain decrypted input.</param>
        /// <param name="cancellationToken">Token that cancels startup.</param>
        /// <returns>The endpoint bound by the engine.</returns>
        public async Task<EndPoint?> StartAsync(SimpleWServer server, ArrayPool<byte> bufferPool, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(bufferPool);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            if (_reactors.Length != 0) {
                throw new InvalidOperationException("The ioxide engine is already running.");
            }

            IPEndPoint endpoint = ValidateEndpoint(server.EndPoint);
            ServerConfig config = CreateConfig(_options.ServerConfig, endpoint);
            TlsOptions? tlsOptions = ValidateTlsOptions(_options.Tls);

            int tlsStartMarker = tlsOptions == null ? 0 : 1;
            if (Interlocked.CompareExchange(ref _tlsStartAttempted, tlsStartMarker, 0) != 0) {
                throw new InvalidOperationException(
                    "An ioxide TLS engine cannot be started twice because ioxide 0.4.184 does not expose TlsService disposal."
                );
            }

            _server = server;
            _endpoint = endpoint;
            _bufferPool = bufferPool;
            _deferFlush = _options.FlushPolicy == IoxideFlushPolicy.EndOfReadBatch;
            _tlsEnabled = tlsOptions != null;
            _reactors = new Reactor[config.ReactorCount];
            _threads = new Thread[config.ReactorCount];
            TaskCompletionSource<bool>[] startupSignals = new TaskCompletionSource<bool>[config.ReactorCount];

            try {
                for (int index = 0; index < config.ReactorCount; index++) {
                    TaskCompletionSource<bool> startupSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Reactor reactor = new(index, config);
                    reactor.OnStart = current => InitializeReactor(current, tlsOptions, startupSignal);
                    reactor.TcpHandle = HandleConnectionAsync;

                    Thread thread = new(() => RunReactor(reactor, startupSignal)) {
                        Name = $"simplew-ioxide-{index}",
                        IsBackground = true
                    };

                    startupSignals[index] = startupSignal;
                    _reactors[index] = reactor;
                    _threads[index] = thread;
                    thread.Start();
                }

                await Task.WhenAll(startupSignals.Select(static signal => signal.Task))
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return endpoint;
            }
            catch {
                StopAndJoinReactors();
                ResetRuntimeState();
                throw;
            }
        }

        private void InitializeReactor(Reactor reactor, TlsOptions? tlsOptions, TaskCompletionSource<bool> startupSignal) {
            try {
                if (tlsOptions != null) {
                    TlsService.Start(reactor, tlsOptions);
                }
                _onReactorStart?.Invoke(reactor);
                startupSignal.TrySetResult(true);
            }
            catch (Exception ex) {
                startupSignal.TrySetException(ex);
                reactor.Stop();
            }
        }

        private async Task HandleConnectionAsync(Reactor reactor, TcpConnection connection) {
            SimpleWServer? server = _server;
            IPEndPoint? endpoint = _endpoint;
            ArrayPool<byte>? bufferPool = _bufferPool;
            if (server == null || endpoint == null || bufferPool == null || server.IsStopping) {
                connection.DecRef();
                return;
            }

            IoxideRawConnection? rawTransport = null;
            IoxideTlsConnection? tlsTransport = null;
            TlsSession? pendingTlsSession = null;

            try {
                EndPoint localEndPoint = new IPEndPoint(endpoint.Address, connection.ListenerPort);
                if (!_tlsEnabled) {
                    rawTransport = new IoxideRawConnection(connection, localEndPoint, _deferFlush);
                    await server.CreateSessionAsync(rawTransport);
                }
                else {
                    pendingTlsSession = await reactor.GetService<TlsService>().AcceptAsync(connection);
                    tlsTransport = new IoxideTlsConnection(
                        connection,
                        pendingTlsSession,
                        localEndPoint,
                        bufferPool,
                        _deferFlush
                    );
                    pendingTlsSession = null;
                    await server.CreateSessionAsync(tlsTransport);
                }
            }
            catch (Exception ex) {
                if (_tlsEnabled) {
                    Console.Error.WriteLine($"[ioxide-tls] connection failed: {ex.Message}");
                }
            }
            finally {
                try {
                    pendingTlsSession?.Dispose();
                    rawTransport?.Complete();
                    tlsTransport?.Complete();
                }
                finally {
                    connection.DecRef();
                }
            }
        }

        private static void RunReactor(Reactor reactor, TaskCompletionSource<bool> startupSignal) {
            try {
                reactor.Run();
            }
            catch (Exception ex) {
                startupSignal.TrySetException(ex);
                Console.Error.WriteLine(ex);
            }
        }

        /// <summary>
        /// Closes active SimpleW sessions and stops all ioxide reactors.
        /// </summary>
        /// <param name="server">SimpleW server served by this engine.</param>
        /// <param name="cancellationToken">Token that cancels the stop request before it begins.</param>
        /// <returns>A task that completes after every reactor thread has stopped.</returns>
        public Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();

            try { server.CloseAllSessions(); }
            catch { }

            StopAndJoinReactors();
            ResetRuntimeState();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the reactors and releases the managed resources owned by this engine.
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            StopAndJoinReactors();
            ResetRuntimeState();
        }

        private void StopAndJoinReactors() {
            foreach (Reactor reactor in _reactors) {
                if (reactor == null) {
                    continue;
                }
                try { reactor.Stop(); }
                catch { }
            }

            foreach (Thread thread in _threads) {
                if (thread == null) {
                    continue;
                }
                try {
                    if (thread.IsAlive && thread.ManagedThreadId != Environment.CurrentManagedThreadId) {
                        thread.Join();
                    }
                }
                catch { }
            }
        }

        private void ResetRuntimeState() {
            _reactors = [];
            _threads = [];
            _server = null;
            _endpoint = null;
            _bufferPool = null;
            _tlsEnabled = false;
        }

        private static IPEndPoint ValidateEndpoint(EndPoint endPoint) {
            if (!OperatingSystem.IsLinux()) {
                throw new PlatformNotSupportedException("SimpleW.Engine.Ioxide requires Linux.");
            }
            if (endPoint is not IPEndPoint endpoint) {
                throw new NotSupportedException("Ioxide requires an IPEndPoint.");
            }
            if (endpoint.Port <= 0 || endpoint.Port > ushort.MaxValue) {
                throw new ArgumentOutOfRangeException(nameof(endPoint), "Port must be between 1 and 65535.");
            }
            if (!endpoint.Address.Equals(IPAddress.Any) && !endpoint.Address.Equals(IPAddress.IPv6Any)) {
                throw new NotSupportedException("Use IPAddress.Any or IPAddress.IPv6Any with ioxide.");
            }
            return endpoint;
        }

        private static ServerConfig CreateConfig(ServerConfig source, IPEndPoint endpoint) {
            ArgumentNullException.ThrowIfNull(source);
            if (source.ReactorCount <= 0) {
                throw new ArgumentOutOfRangeException(nameof(source), "ReactorCount must be greater than zero.");
            }

            TcpOptions tcp = source.Tcp ?? new TcpOptions();
            return source with {
                DualStack = endpoint.Address.Equals(IPAddress.IPv6Any),
                Udp = null,
                Quic = null,
                Tcp = tcp with {
                    Port = checked((ushort)endpoint.Port)
                }
            };
        }

        private static TlsOptions? ValidateTlsOptions(TlsOptions? tlsOptions) {
            if (tlsOptions == null) {
                return null;
            }
            if (tlsOptions.KernelTx || tlsOptions.KernelRx) {
                throw new NotSupportedException(
                    "SimpleW.Engine.Ioxide currently supports only OpenSSL userspace TLS. KernelTx and KernelRx must be false."
                );
            }
            if (tlsOptions.Alpn == null
                || tlsOptions.Alpn.Length != 1
                || !string.Equals(tlsOptions.Alpn[0], "http/1.1", StringComparison.Ordinal)) {
                throw new NotSupportedException(
                    "SimpleW.Engine.Ioxide supports HTTP/1.x only. TlsOptions.Alpn must be exactly [\"http/1.1\"]."
                );
            }
            return tlsOptions;
        }

    }

}
