using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using SimpleW.Modules;
using SimpleW.Observability;
using SimpleW.Security;


namespace SimpleW {

    /// <summary>
    /// SimpleW Server
    /// </summary>
    public class SimpleWServer {

        /// <summary>
        /// Router
        /// </summary>
        public Router Router { get; private set; }

        #region constructor

        /// <summary>
        /// Initialize TCP server with a given IP address and port number
        /// </summary>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        public SimpleWServer(IPAddress address, int port) : this(new IPEndPoint(address, port)) { }

        /// <summary>
        /// Initialize TCP server with a given IP endpoint
        /// </summary>
        /// <param name="endpoint">IP endpoint</param>
        public SimpleWServer(EndPoint endpoint) {
            EndPoint = endpoint;
            Router = new Router();
            Options = new SimpleWSServerOptions();
        }

        #endregion constructor

        #region actions

        /// <summary>
        /// Options
        /// </summary>
        internal SimpleWSServerOptions Options { get; private set; }

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
        /// Configure
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <example>
        /// server.Configure(options => {
        ///     options.ReuseAddress = true;
        ///     options.TcpNoDelay = true;
        ///     options.TcpKeepAlive = true;
        /// });
        /// </example>
        public SimpleWServer Configure(Action<SimpleWSServerOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            if (IsStarted) {
                throw new InvalidOperationException("Server options must be configured before starting the server.");
            }

            configure(Options);
            return this;
        }

        /// <summary>
        /// Start the server (not blocking)
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <example>await server.StartAsync(appLifetime.ApplicationStopping);</example>
        public Task StartAsync(CancellationToken cancellationToken = default) {
            if (IsStarted) {
                return Task.CompletedTask;
            }

            Options.ValidateAndNormalize();

            _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            IsStopping = false;

            ListenSocket();
            StartSessionTimeoutTimer();

            IsStarted = true;
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

        #region middleware and module

        /// <summary>
        /// Add a new Middleware
        /// </summary>
        /// <param name="middleware"></param>
        /// <example>
        /// // add simple logging
        /// server.UseMiddleware(static async (session, next) => {
        ///     var sw = System.Diagnostics.Stopwatch.StartNew();
        ///     try {
        ///         await next();
        ///     }
        ///     finally {
        ///         sw.Stop();
        ///         Console.WriteLine($"[{DateTime.UtcNow:O}] {session.Request.Method} {session.Request.Path} in {sw.ElapsedMilliseconds} ms");
        ///     }
        /// });
        /// // add firewall/auth
        /// server.UseMiddleware(static (session, next) => {
        ///     if (session.Request.Path.StartsWith("/api/secret", StringComparison.Ordinal)) {
        ///         if (!session.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != "secret") {
        ///             return session.SendTextAsync("Unauthorized", 401, "Unauthorized");
        ///         }
        ///     }
        ///     return next();
        /// });
        /// </example>
        public void UseMiddleware(HttpMiddleware middleware) => Router.UseMiddleware(middleware);

        /// <summary>
        /// Add a new Module
        /// </summary>
        /// <param name="module"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <example>
        /// // declare a test module
        /// public sealed class TestModule : IHttpModule {
        ///     public void Install(SimpleW server) {
        ///         server.MapGet("/api/test/hello", static (session) => {
        ///             return session.SendTextAsync("Hello World !");
        ///         }
        ///     }
        /// }
        /// // just instanciate test module in SimpleW and that's it
        /// server.UseModule(new TestModule())
        /// 
        /// // for a cleaner API call, add an extension
        /// public static class SimpleWServerTestModuleExtensions {
        ///     public static SimpleWServer UseTestModule(this SimpleWServer server) {
        ///         server.UseModule(new TestModule());
        ///         return server;
        ///     }
        /// }
        /// // now the module has a nice use method
        /// server.UseTestModule();
        /// </example>
        public void UseModule(IHttpModule module) {
            ArgumentNullException.ThrowIfNull(module);
            module.Install(this);
        }

        #endregion middleware and module

        #region handler result

        /// <summary>
        /// Override HandlerResult, Action to do for the non nulls returns
        /// </summary>
        /// <param name="handler"></param>
        /// <example>
        /// // example1 : log and sent
        /// server.ConfigureHandlerResult((session, result) => {
        ///     Console.WriteLine("result will be serialized and sent as response");
        ///     return session.SendJsonAsync(result);
        /// });
        /// // exemple2: do something async and sent response
        /// server.ConfigureHandlerResult(async (session, result) => {
        ///     Console.WriteLine("wait 2sec then result will be serialized and sent as response");
        ///     await Task.Delay(2_000);
        ///     await session.SendJsonAsync(result);
        /// });
        /// </example>
        public SimpleWServer ConfigureHandlerResult(HttpHandlerResult handler) {
            Router.HandlerResult = handler;
            return this;
        }

        #endregion handler result

        #region map delegate

        /// <summary>
        /// Add handler for Method/Path
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public SimpleWServer Map(string method, string path, Delegate handler) {
            Router.Map(method, path, handler);
            return this;
        }

        /// <summary>
        /// Add handler for GET request
        /// alias for Map("GET", path, handler)
        /// </summary>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <example>
        /// server.MapGet("/api/test/hello", static () => {
        ///     return new { message = "Hello World !" };
        /// });
        /// server.MapGet("/api/test/hello", static (string? name = null) => {
        ///     return new { message = $"Hello {name} !" };
        /// });
        /// server.MapGet("/api/test/:name", (string? name = null) => {
        ///     return new { message = $"Hello {name} !" };
        /// });
        /// server.MapGet("/api/test/hello", static (HttpSession session) => {
        ///     return session.SendJsonAsync(new { message = "Hello World !" });
        /// });
        /// server.MapGet("/api/test/hello", static (HttpSession session, string? name = null) => {
        ///     return session.SendJsonAsync(new { message = $"Hello {name} !" });
        /// });
        /// server.MapGet("/api/test/hello", static (string? name = null, HttpSession session) => {
        ///     return session.SendJsonAsync(new { message = $"Hello {name} !" });
        /// });
        /// server.MapGet("/api/test/hello", static async (HttpSession session, string? name = null) => {
        ///     await Task.Delay(2_000);
        ///     await session.SendJsonAsync(new { message = $"Hello {name} !" });
        /// });
        /// server.MapGet("/api/test/hello", static async (string? name = null) => {
        ///     await Task.Delay(2_000);
        ///     return new { message = $"Hello {name} !" };
        /// });
        /// server.MapGet("/api/test/hello", static object (HttpSession session,string? name = null) => {
        ///     if (string.IsNullOrWhiteSpace(name)); {
        ///         return Session.Response.Status(404).Text("you must set a name parameter");
        ///     }
        ///     return new { message = $"Hello {name} !" };
        /// });
        /// server.MapGet("/api/test/hello", static async ValueTask&lt;object&gt; (HttpSession session,string? name = null) => {
        ///     if (string.IsNullOrWhiteSpace(name)); {
        ///         await Task.Delay(2_000);
        ///         return Session.Response.Status(404).Text("you must set a name parameter");
        ///     }
        ///     return new { message = $"Hello {name} !" };
        /// });
        /// </example>
        public SimpleWServer MapGet(string path, Delegate handler) {
            Router.MapGet(path, handler);
            return this;
        }

        /// <summary>
        /// Add handler for POST request
        /// alias for Map("POST", path, handler)
        /// </summary>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        public SimpleWServer MapPost(string path, Delegate handler) {
            Router.MapPost(path, handler);
            return this;
        }

        #endregion map delegate

        #region map controllers

        /// <summary>
        /// Register a Controller type and map all its routes
        /// </summary>
        /// <typeparam name="TController"></typeparam>
        /// <param name="basePrefix">Optional base prefix like "/api". Can be null or empty.</param>
        /// <returns></returns>
        public SimpleWServer MapController<TController>(string? basePrefix = null) where TController : Controller {
            RouteExecutorFactory.RegisterController(typeof(TController), Router, basePrefix);
            return this;
        }

        /// <summary>
        /// Register all controllers assignable to TController found in the same assembly
        /// </summary>
        /// <typeparam name="TController"></typeparam>
        /// <param name="basePrefix"></param>
        /// <param name="excludes"></param>
        /// <returns></returns>
        /// <example>
        /// <code>
        /// server.MapControllers&lt;Controller&gt;(
        ///     "/api",
        ///     new[] { typeof(MaintenanceController) }
        /// );
        /// </code>
        /// </example>
        public SimpleWServer MapControllers<TController>(string? basePrefix = null, IEnumerable<Type>? excludes = null) where TController : Controller {
            Type baseType = typeof(TController);
            HashSet<Type> excluded = new(excludes ?? Enumerable.Empty<Type>());

            foreach (Type type in AppDomain.CurrentDomain.GetAssemblies()
                                                         .SelectMany(a => {
                                                            try { return a.GetTypes(); }
                                                            catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>(); }
                                                         })
                                                         .Where(t => t != null
                                                                     && !t.IsAbstract
                                                                     && t != baseType
                                                                     && baseType.IsAssignableFrom(t)
                                                                     && !excluded.Contains(t))
            ) {
                RouteExecutorFactory.RegisterController(type, Router, basePrefix);
            }

            return this;
        }

        #endregion map controllers

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

            // create socket
            _listenSocket = CreateSocket();

            // option: reuse address
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, Options.ReuseAddress);
            // option: exclusive address use
            _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, Options.ExclusiveAddressUse);
            // option: reuse port
            if (Options.ReusePort) {
                _listenSocket.EnableReusePort();
            }
            // option: dual mode (this option must be applied before listening)
            if (EndPoint.AddressFamily == AddressFamily.InterNetworkV6) {
                _listenSocket.DualMode = Options.DualMode;
            }

            // bind socket to endpoint
            _listenSocket.Bind(EndPoint);
            // refresh the endpoint property based on the actual endpoint created
            EndPoint = _listenSocket.LocalEndPoint!;
            // start listen to the socket with the given accepting backlog size
            _listenSocket.Listen(Options.ListenBacklog);

            int maxParalleListenSocketEventArgs = Options.AcceptPerCore ? Environment.ProcessorCount : 1;
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
        public SimpleWServer UseHttps(SslContext sslContext) {
            if (IsStarted) {
                throw new InvalidOperationException("SslContext must be configured before starting the server.");
            }
            SslContext = sslContext;
            return this;
        }

        #endregion sslcontext

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
                connection.Connect()                    ;                   // receive data
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
            if (Options.SessionTimeout == TimeSpan.MinValue) {
                return;
            }
            session.MarkActivity();
        }

        #region idle timeout

        /// <summary>
        /// Timer for Idel timeout
        /// </summary>
        private Timer? _sessionTimeoutTimer;

        /// <summary>
        /// Start Session Timeout Timer
        /// </summary>
        private void StartSessionTimeoutTimer() {
            if (Options.SessionTimeout == TimeSpan.MinValue) {
                return;
            }
            if (_sessionTimeoutTimer != null) {
                return;
            }

            double seconds = Math.Min(5, Options.SessionTimeout.TotalSeconds / 2);
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
                long timeoutMs = (long)Options.SessionTimeout.TotalMilliseconds;

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

        #region json engine

        /// <summary>
        /// Json Serializer/Deserializer
        /// </summary>
        public IJsonEngine JsonEngine { get; private set; } = new SystemTextJsonEngine(SystemTextJsonEngine.OptionsSimpleWBuilder());

        /// <summary>
        /// Set the Json Serializer/Deserializer
        /// </summary>
        /// <param name="jsonEngine"></param>
        /// <returns></returns>
        public SimpleWServer ConfigureJsonEngine(IJsonEngine jsonEngine) {
            if (IsStarted) {
                throw new InvalidOperationException("JsonEngine must be configured before starting the server.");
            }
            JsonEngine = jsonEngine;
            return this;
        }

        #endregion json engine

        #region telemetry

        /// <summary>
        /// Status of Telemetry
        /// </summary>
        public bool IsTelemetryEnabled => Telemetry.Enabled;

        /// <summary>
        /// Configure Telemetry
        /// </summary>
        /// <param name="configure"></param>
        /// <example>
        /// server.ConfigureTelemetry(options => {
        ///     options.IncludeStackTrace = true;
        /// });
        /// </example>
        /// <returns></returns>
        public SimpleWServer ConfigureTelemetry(Action<TelemetryOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);
            configure(Telemetry.Options);
            return this;
        }

        /// <summary>
        /// Enable Telemetry
        /// </summary>
        /// <returns></returns>
        public SimpleWServer EnableTelemetry() {
            Telemetry.Enable();
            return this;
        }

        /// <summary>
        /// Disable Telemetry
        /// </summary>
        /// <returns></returns>
        public SimpleWServer DisableTelemetry() {
            Telemetry.Disable();
            return this;
        }


        #endregion telemetry

        #region jwt

        /// <summary>
        /// Get the JwtResolver, by priority :
        ///    1. Request url querystring "jwt"
        ///    2. Request http header "Authorization: bearer "
        ///    3. SecWebSocketProtocol "bearer, TOKEN" (websocket only)
        /// </summary>
        public JwtResolver JwtResolver { get; private set; } = (request) => {

            // 1. Request url querystring "jwt"
            if (request.Query.TryGetValue("jwt", out string? qs_jwt) && !string.IsNullOrWhiteSpace(qs_jwt)) {
                return qs_jwt;
            }

            // 2. Request http header "Authorization: bearer "
            if (request.Headers.Authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ?? false) {
                return request.Headers.Authorization["Bearer ".Length..];
            }

            // 3. SecWebSocketProtocol "bearer, TOKEN" (websocket only)
            if (request.Headers.SecWebSocketVersion == "13"
                && (request.Headers.SecWebSocketProtocol?.StartsWith("Bearer, ", StringComparison.OrdinalIgnoreCase) ?? false)
            ) {
                return request.Headers.SecWebSocketProtocol["Bearer, ".Length..];
            }

            return null;
        };

        /// <summary>
        /// Configure the JwtResolver
        /// </summary>
        /// <param name="jwtResolver"></param>
        /// <returns></returns>
        /// <example>
        /// server.ConfigureJwtResolver(request => {
        ///     options.IncludeStackTrace = true;
        /// });
        /// </example>
        public SimpleWServer ConfigureJwtResolver(JwtResolver jwtResolver) {
            JwtResolver = jwtResolver;
            return this;
        }

        #endregion jwt

        #region user

        /// <summary>
        /// Get the UserResolver
        /// </summary>
        internal WebUserResolver UserResolver { get; private set; } = WebUserResolvers.TokenWebUser;

        /// <summary>
        /// Configure the UserResolver
        /// </summary>
        /// <param name="userResolver"></param>
        /// <returns></returns>
        public SimpleWServer ConfigureUserResolver(WebUserResolver userResolver) {
            UserResolver = userResolver;
            return this;
        }

        #endregion user

    }

    /// <summary>
    /// SimpleWSServer Options
    /// </summary>
    public sealed class SimpleWSServerOptions {

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
        /// JwtOptions
        /// </summary>
        public JwtOptions? JwtOptions { get; set; }

        #endregion security

        #region socket

        /// <summary>
        /// This option will set the maximum length of the pending connections queue.
        /// </summary>
        public int ListenBacklog { get; set; } = 1024;

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

        #endregion socket

        #region session

        /// <summary>
        /// Idle timeout (if no data received during timeout, then close connection)
        /// Set TimeSpan.MinValue to disable.
        /// </summary>
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        #endregion session

        /// <summary>
        /// Check Properties and return
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public SimpleWSServerOptions ValidateAndNormalize() {

            // basic ranges
            if (MaxRequestHeaderSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxRequestHeaderSize), "Must be > 0.");
            }
            if (MaxRequestBodySize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxRequestBodySize), "Must be > 0.");
            }
            if (ListenBacklog <= 0) {
                throw new ArgumentOutOfRangeException(nameof(ListenBacklog), "Must be > 0.");
            }
            if (ReceiveBufferSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(ReceiveBufferSize), "Must be > 0.");
            }

            // sanity checks
            if (ReuseAddress && ExclusiveAddressUse) {
                throw new ArgumentException($"{nameof(ReuseAddress)} and {nameof(ExclusiveAddressUse)} are mutually exclusive.");
            }
            if (ReusePort) {
                if (!OperatingSystem.IsLinux()) {
                    throw new PlatformNotSupportedException($"{nameof(ReusePort)} is only supported on Linux.");
                }
                if (!AcceptPerCore) {
                    throw new ArgumentException($"{nameof(ReusePort)} is only useful on Linux when {nameof(AcceptPerCore)} is enabled.");
                }
            }

            // normalize cache/timeout-like values
            // (keep your existing convention)
            // - MinValue => disabled (do not change)
            // - Negative other than MinValue => not allowed
            if (SessionTimeout != TimeSpan.MinValue && SessionTimeout < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(SessionTimeout), "Must be >= 0 or TimeSpan.MinValue to disable.");
            }

            return this;
        }

    }

}
