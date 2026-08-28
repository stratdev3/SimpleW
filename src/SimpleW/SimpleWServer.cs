using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW {

    /// <summary>
    /// SimpleW Server
    /// </summary>
    public class SimpleWServer {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<SimpleWServer>();

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
            ControllerActionExecutorFactory = RouteExecutorFactory.CreateControllerActionExecutor;
            Engine = new SimpleWEngine();
            Options = new SimpleWSServerOptions();
        }

        #endregion constructor

        #region options

        /// <summary>
        /// Options
        /// </summary>
        internal SimpleWSServerOptions Options { get; private set; }

        /// <summary>
        /// Configure
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <example>
        /// server.Configure(options => {
        ///     options.SessionTimeout = TimeSpan.FromSeconds(30);
        /// });
        /// </example>
        public SimpleWServer Configure(Action<SimpleWSServerOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            lock (_stateLock) {
                EnsureStateStopped("Server options must be configured before starting the server.");
                configure(Options);
            }
            return this;
        }

        #endregion options

        #region endpoint

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
        /// Use IPAddress
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public SimpleWServer UseAddress(IPAddress address) {
            lock (_stateLock) {
                EnsureStateStoppedOrReloading("IPAddress must be configured before starting the server (except during listener reload).");
                if (Port == 0) {
                    InvalidOperationException ex = new("Unable to find the current port.");
                    _log.Error(ex.Message, ex);
                    throw ex;
                }
                EndPoint = new IPEndPoint(address, Port);
            }
            return this;
        }

        /// <summary>
        /// Use Port
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public SimpleWServer UsePort(int port) {
            lock (_stateLock) {
                EnsureStateStoppedOrReloading("Port must be configured before starting the server (except during listener reload).");
                if (Address == null) {
                    InvalidOperationException ex = new("Unable to find the current IPAddress.");
                    _log.Error(ex.Message, ex);
                    throw ex;
                }
                EndPoint = new IPEndPoint(Address, port);
            }
            return this;
        }

        /// <summary>
        /// listen url
        /// </summary>
        private string _listenUrl {
            get {
                string scheme = Engine.IsEncrypted ? "https" : "http";
                bool defaultPort = (Engine.IsEncrypted && Port == 443) || (!Engine.IsEncrypted && Port == 80);
                return $"{scheme}://{Address?.ToString()}{(defaultPort ? "" : $":{Port}")}";
            }
        }

        #endregion endpoint

        #region engine

        /// <summary>
        /// Network engine used by the server.
        /// </summary>
        public ISimpleWEngine Engine { get; private set; }

        /// <summary>
        /// Current server lifecycle state.
        /// </summary>
        public SimpleWServerState State {
            get {
                lock (_stateLock) {
                    return _state;
                }
            }
        }

        /// <summary>
        /// Synchronizes state transitions with synchronous configuration methods.
        /// </summary>
        private readonly object _stateLock = new();

        /// <summary>
        /// Current state guarded by _stateLock.
        /// </summary>
        private SimpleWServerState _state = SimpleWServerState.Stopped;

        /// <summary>
        /// Serializes all asynchronous lifecycle operations.
        /// </summary>
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

        /// <summary>
        /// Identifies lifecycle calls made from state change callbacks.
        /// </summary>
        private readonly AsyncLocal<int> _lifecycleCallbackDepth = new();

        /// <summary>
        /// Lifetime CTS (server internal)
        /// </summary>
        private CancellationTokenSource? _lifetimeCts;

        /// <summary>
        /// Task main server loop
        /// </summary>
        private Task? _lifetimeTask;

        /// <summary>
        /// Current server lifetime generation.
        /// </summary>
        private long _lifetimeGeneration;

        /// <summary>
        /// Ensure that state is stopped
        /// Must be called while holding _stateLock.
        /// </summary>
        /// <param name="message"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnsureStateStopped(string message) {
            if (_state != SimpleWServerState.Stopped) {
                InvalidOperationException ex = new(message);
                _log.Warn(ex.Message, ex);
                throw ex;
            }
        }

        /// <summary>
        /// Ensure that state is stopped or reloading
        /// Must be called while holding _stateLock.
        /// </summary>
        /// <param name="message"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnsureStateStoppedOrReloading(string message) {
            if (_state != SimpleWServerState.Stopped && _state != SimpleWServerState.Reloading) {
                InvalidOperationException ex = new(message);
                _log.Warn(ex.Message, ex);
                throw ex;
            }
        }

        /// <summary>
        /// Lifecycle callbacks are part of the serialized transition and cannot re-enter it.
        /// We must prevent a callback to call a method changing the state (e.g: StartAsync()...)
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ThrowIfLifecycleCallback() {
            if (_lifecycleCallbackDepth.Value != 0) {
                throw new InvalidOperationException("Server lifecycle methods cannot be called from a state change callback.");
            }
        }

        /// <summary>
        /// Replace the current network engine implementation.
        /// Must be called before the server starts.
        /// </summary>
        /// <param name="engine"></param>
        /// <returns></returns>
        public SimpleWServer UseEngine(ISimpleWEngine engine) {
            ArgumentNullException.ThrowIfNull(engine);

            lock (_stateLock) {
                EnsureStateStopped("Engine must be configured before starting the server.");
                Engine = engine;
            }
            return this;
        }

        /// <summary>
        /// Configure and use the default socket-based network engine.
        /// Must be called before the server starts.
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public SimpleWServer UseEngine(Action<SimpleWEngineOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            lock (_stateLock) {
                EnsureStateStopped("Engine must be configured before starting the server.");
                SimpleWEngineOptions options = new();
                configure(options);
                Engine = new SimpleWEngine(options);
            }
            return this;
        }

        /// <summary>
        /// Start the server (not blocking)
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <example>await server.StartAsync(appLifetime.ApplicationStopping);</example>
        public async Task StartAsync(CancellationToken cancellationToken = default) {
            _ = await StartAndGetLifetimeTaskAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Run the server (blocking)
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <example>await server.RunAsync(cts.Token);</example>
        public async Task RunAsync(CancellationToken cancellationToken = default) {
            Task lifetimeTask = await StartAndGetLifetimeTaskAsync(cancellationToken).ConfigureAwait(false);
            await lifetimeTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Start the server and return the completion task for the selected lifetime generation.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<Task> StartAndGetLifetimeTaskAsync(CancellationToken cancellationToken) {
            ThrowIfLifecycleCallback();

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                SimpleWServerState state = State;
                if (state == SimpleWServerState.Started) {
                    return _lifetimeTask ?? Task.CompletedTask;
                }
                if (state == SimpleWServerState.Faulted) {
                    throw new InvalidOperationException("The server is faulted. Call StopAsync successfully before restarting it.");
                }
                if (state != SimpleWServerState.Stopped) {
                    throw new InvalidOperationException($"The server cannot start from state '{state}'.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                CancellationTokenSource lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                long generation = unchecked(++_lifetimeGeneration);
                _lifetimeCts = lifetimeCts;
                await ChangeStateAsync(SimpleWServerState.Starting).ConfigureAwait(false);

                bool engineStarted = false;
                try {
                    _log.Info("server starting...");
                    Options.ValidateAndNormalize();

                    if (_telemetryOptions.Enabled) {
                        Telemetry = new Telemetry(_telemetryOptions);
                    }

                    // refresh the endpoint property based on the actual endpoint created
                    EndPoint? startedEndPoint = await Engine.StartAsync(this, _bufferPool, lifetimeCts.Token).ConfigureAwait(false);
                    engineStarted = true;
                    StartSessionTimeoutLoop(lifetimeCts.Token);

                    lock (_stateLock) {
                        EndPoint = startedEndPoint ?? EndPoint;
                    }

                    Task lifetimeTask = WaitForCancellationAsync(generation, lifetimeCts);
                    _lifetimeTask = lifetimeTask;
                    _log.Info($"server started at {_listenUrl}");
                    await ChangeStateAsync(SimpleWServerState.Started).ConfigureAwait(false);
                    return lifetimeTask;
                }
                catch (Exception startException) {
                    Exception? cleanupException = null;
                    if (engineStarted) {
                        try {
                            await Engine.StopAsync(this, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex) {
                            cleanupException = ex;
                            _log.Fatal("server failed to clean up after start failure", ex);
                        }
                    }

                    try { lifetimeCts.Cancel(); }
                    catch { }
                    await StopSessionTimeoutLoopAsync().ConfigureAwait(false);

                    try { Telemetry?.Dispose(); }
                    catch { }
                    Telemetry = null;

                    lifetimeCts.Dispose();
                    if (ReferenceEquals(_lifetimeCts, lifetimeCts)) {
                        _lifetimeCts = null;
                        _lifetimeTask = null;
                    }
                    await ChangeStateAsync(cleanupException == null ? SimpleWServerState.Stopped : SimpleWServerState.Faulted).ConfigureAwait(false);

                    if (cleanupException != null) {
                        throw new AggregateException("Server start and cleanup both failed.", startException, cleanupException);
                    }
                    throw;
                }
            }
            finally {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// Wait for cancellation to stop the server
        /// </summary>
        /// <param name="generation"></param>
        /// <param name="lifetimeCts"></param>
        /// <returns></returns>
        private async Task WaitForCancellationAsync(long generation, CancellationTokenSource lifetimeCts) {
            try {
                await Task.Delay(Timeout.Infinite, lifetimeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // normal
            }

            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try {
                // Ignore cancellation from an obsolete server generation.
                if (generation != _lifetimeGeneration || !ReferenceEquals(_lifetimeCts, lifetimeCts)) {
                    return;
                }
                await StopWhileLifecycleLockedAsync().ConfigureAwait(false);
            }
            finally {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// Stop Server
        /// </summary>
        /// <returns></returns>
        public async Task StopAsync() {
            ThrowIfLifecycleCallback();
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try {
                await StopWhileLifecycleLockedAsync().ConfigureAwait(false);
            }
            finally {
                _lifecycleLock.Release();
            }
        }

        /// <summary>
        /// Stop the current generation. Must be called while holding _lifecycleLock.
        /// </summary>
        /// <returns></returns>
        private async Task StopWhileLifecycleLockedAsync() {
            SimpleWServerState state = State;
            if (state == SimpleWServerState.Stopped) {
                return;
            }
            if (state != SimpleWServerState.Started && state != SimpleWServerState.Faulted) {
                throw new InvalidOperationException($"The server cannot stop from state '{state}'.");
            }

            await ChangeStateAsync(SimpleWServerState.Stopping).ConfigureAwait(false);
            _log.Info($"server stopping {_listenUrl} ...");

            CancellationTokenSource? lifetimeCts = _lifetimeCts;
            try { lifetimeCts?.Cancel(); }
            catch { }

            bool engineStopFailed = false;
            try {
                await Engine.StopAsync(this, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) {
                engineStopFailed = true;
                _log.Fatal("server engine failed to stop", ex);
                throw;
            }
            finally {
                CloseAllSessions();
                await StopSessionTimeoutLoopAsync().ConfigureAwait(false);

                try { Telemetry?.Dispose(); }
                catch { }
                Telemetry = null;

                if (ReferenceEquals(_lifetimeCts, lifetimeCts)) {
                    _lifetimeCts = null;
                    _lifetimeTask = null;
                }
                lifetimeCts?.Dispose();

                if (engineStopFailed) {
                    await ChangeStateAsync(SimpleWServerState.Faulted).ConfigureAwait(false);
                }
            }

            await ChangeStateAsync(SimpleWServerState.Stopped).ConfigureAwait(false);
            _log.Info($"server stopped {_listenUrl}");
        }

        /// <summary>
        /// Reload the listener socket without stopping the server lifetime.
        /// Does NOT close existing client sessions; only affects new incoming connections.
        /// <example>
        /// await server.ReloadListenerAsync(s => {
        ///     if (s.Engine is SimpleWEngine engine) {
        ///         engine.UseHttps(newSslContext);
        ///     }
        ///     s.UsePort(80);
        /// });
        /// </example>
        /// </summary>
        /// <exception cref="ListenerReloadException">The listener reload failed. Inspect the exception to determine whether the previous listener was restored.</exception>
        public async Task ReloadListenerAsync(Action<SimpleWServer> reconfigure, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(reconfigure);
            ThrowIfLifecycleCallback();

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                SimpleWServerState state = State;
                if (state != SimpleWServerState.Started) {
                    InvalidOperationException ex = new($"The server must be started to reload the listener (current state: '{state}').");
                    _log.Error(ex.Message, ex);
                    throw ex;
                }

                EndPoint oldEndPoint;
                lock (_stateLock) {
                    oldEndPoint = EndPoint;
                }
                await ChangeStateAsync(SimpleWServerState.Reloading).ConfigureAwait(false);
                _log.Info($"server reloading {_listenUrl}...");

                try {
                    // stop accepting new connections
                    await Engine.StopAsync(this, cancellationToken).ConfigureAwait(false);
                    // callback
                    reconfigure(this);
                    // new listener + restart accept loop(s)
                    EndPoint? reloadedEndPoint = await Engine.StartAsync(this, _bufferPool, cancellationToken).ConfigureAwait(false);
                    lock (_stateLock) {
                        EndPoint = reloadedEndPoint ?? EndPoint;
                    }
                    _log.Warn($"server reloaded at {_listenUrl}");
                    await ChangeStateAsync(SimpleWServerState.Started).ConfigureAwait(false);
                }
                catch (Exception exReload) {
                    _log.Warn($"server failed to reload at {_listenUrl}", exReload);
                    // rollback best effort
                    try {
                        await Engine.StopAsync(this, CancellationToken.None).ConfigureAwait(false);
                        lock (_stateLock) {
                            EndPoint = oldEndPoint;
                        }
                        _log.Warn($"server restoring at {_listenUrl}", exReload);
                        EndPoint? restoredEndPoint = await Engine.StartAsync(this, _bufferPool, CancellationToken.None).ConfigureAwait(false);
                        lock (_stateLock) {
                            EndPoint = restoredEndPoint ?? EndPoint;
                        }
                        _log.Warn($"server restored at {_listenUrl}");
                        await ChangeStateAsync(SimpleWServerState.Started).ConfigureAwait(false);
                    }
                    catch (Exception exRollback) {
                        await ChangeStateAsync(SimpleWServerState.Faulted).ConfigureAwait(false);
                        _log.Fatal($"server failed to restore at {_listenUrl}", exRollback);
                        throw new ListenerReloadException(exReload, exRollback);
                    }
                    throw new ListenerReloadException(exReload);
                }
            }
            finally {
                _lifecycleLock.Release();
            }
        }

        #region callbacks

        // fluent callbacks, stored as a single ordered pipeline
        private readonly List<Func<SimpleWServer, SimpleWServerState, Task>> _onStateChanged = new();

        /// <summary>
        /// Register a callback invoked after each server state transition.
        /// </summary>
        /// <param name="callback"></param>
        /// <returns></returns>
        public SimpleWServer OnStateChanged(Action<SimpleWServer, SimpleWServerState> callback) {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_stateLock) {
                _onStateChanged.Add((server, state) => {
                    callback(server, state);
                    return Task.CompletedTask;
                });
            }
            return this;
        }

        /// <summary>
        /// Register an async callback invoked after each server state transition.
        /// </summary>
        /// <param name="callback"></param>
        /// <returns></returns>
        public SimpleWServer OnStateChanged(Func<SimpleWServer, SimpleWServerState, Task> callback) {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_stateLock) {
                _onStateChanged.Add(callback);
            }
            return this;
        }

        /// <summary>
        /// Change the current state and notify subscribers in registration order.
        /// </summary>
        /// <param name="state"></param>
        private async Task ChangeStateAsync(SimpleWServerState state) {
            Func<SimpleWServer, SimpleWServerState, Task>[] callbacks;
            lock (_stateLock) {
                if (_state == state) {
                    return;
                }
                _state = state;
                callbacks = _onStateChanged.ToArray();
            }

            _lifecycleCallbackDepth.Value++;
            try {
                foreach (Func<SimpleWServer, SimpleWServerState, Task> callback in callbacks) {
                    try {
                        await callback(this, state).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        _log.Error($"server state callback failed for '{state}'", ex);
                    }
                }
            }
            finally {
                _lifecycleCallbackDepth.Value--;
            }
        }

        #endregion callbacks

        #endregion engine

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
        ///             return session.Response.Unauthorized();
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
        /// <exception cref="InvalidOperationException">The server is not stopped.</exception>
        /// <example>
        /// // declare a test module
        /// public sealed class TestModule : IHttpModule {
        ///     public void Install(SimpleW server) {
        ///         server.MapGet("/api/test/hello", static (session) => {
        ///             return session.Response.Text("Hello World !");
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
            lock (_stateLock) {
                EnsureStateStopped("Modules must be installed before starting the server.");
                module.Install(this);
            }
        }

        #endregion middleware and module

        #region handler result

        /// <summary>
        /// Override ResultHandler, Action to do for the non nulls returns
        /// </summary>
        /// <param name="handler"></param>
        /// <example>
        /// // example1 : log and sent
        /// server.ConfigureResultHandler((session, result) => {
        ///     Console.WriteLine("result will be serialized and sent as response");
        ///     return session.Response.Json(result);
        /// });
        /// // exemple2: do something async and sent response
        /// server.ConfigureResultHandler(async (session, result) => {
        ///     Console.WriteLine("wait 2sec then result will be serialized and sent as response");
        ///     await Task.Delay(2_000);
        ///     await session.Response.Json(result);
        /// });
        /// </example>
        public SimpleWServer ConfigureResultHandler(HttpResultHandler handler) {
            Router.ResultHandler = handler;
            return this;
        }

        #endregion handler result

        #region router

        /// <summary>
        /// Router
        /// </summary>
        public IRouter Router { get; private set; }

        /// <summary>
        /// Replace the current router implementation.
        /// All existing routes are lost !!
        /// Must be called before the server starts.
        /// </summary>
        public SimpleWServer UseRouter(IRouter router) {
            ArgumentNullException.ThrowIfNull(router);

            lock (_stateLock) {
                EnsureStateStopped("Router must be configured before starting the server.");
                Router = router;
            }
            return this;
        }

        #endregion router

        #region executor factory

        /// <summary>
        /// Factory used to create controller action executors.
        /// </summary>
        public ControllerActionExecutorFactory ControllerActionExecutorFactory { get; private set; }

        /// <summary>
        /// Replaces the factory used to create controller action executors.
        /// Must be called before the server starts.
        /// </summary>
        /// <param name="factory"></param>
        /// <returns></returns>
        public SimpleWServer UseControllerActionExecutorFactory(ControllerActionExecutorFactory factory) {
            ArgumentNullException.ThrowIfNull(factory);

            lock (_stateLock) {
                EnsureStateStopped("Controller action executor factory must be configured before starting the server.");
                ControllerActionExecutorFactory = factory;
            }
            return this;
        }

        #endregion executor factory

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
        /// Add handler for Method/Host/Path
        /// </summary>
        /// <param name="method"></param>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public SimpleWServer Map(string method, string host, string path, Delegate handler) {
            Router.Map(method, host, path, handler);
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
        ///     return session.Response.Json(new { message = "Hello World !" });
        /// });
        /// server.MapGet("/api/test/hello", static (HttpSession session, string? name = null) => {
        ///     return session.Response.Json(new { message = $"Hello {name} !" });
        /// });
        /// server.MapGet("/api/test/hello", static (string? name = null, HttpSession session) => {
        ///     return session.Response.Json(new { message = $"Hello {name} !" });
        /// });
        /// server.MapGet("/api/test/hello", static async (HttpSession session, string? name = null) => {
        ///     await Task.Delay(2_000);
        ///     await session.Response.Json(new { message = $"Hello {name} !" }).SendAsync();
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
        /// Add handler for GET request
        /// alias for Map("GET", host, path, handler)
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public SimpleWServer MapGet(string host, string path, Delegate handler) {
            Router.MapGet(host, path, handler);
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

        /// <summary>
        /// Add handler for POST request
        /// alias for Map("POST", host, path, handler)
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public SimpleWServer MapPost(string host, string path, Delegate handler) {
            Router.MapPost(host, path, handler);
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
            RouteExecutorFactory.RegisterController(typeof(TController), Router, basePrefix, ControllerActionExecutorFactory);
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
                RouteExecutorFactory.RegisterController(type, Router, basePrefix, ControllerActionExecutorFactory);
            }

            return this;
        }

        #endregion map controllers

        #region session

        /// <summary>
        /// Server sessions
        /// </summary>
        protected readonly ConcurrentDictionary<Guid, HttpSession> Sessions = new();

        /// <summary>
        /// BufferPool
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Handle Transport and Create HttpSession
        /// </summary>
        /// <param name="transport">receive/send transport</param>
        /// <returns></returns>
        public async Task CreateSessionAsync(ISimpleWEngine transport) {

            HttpSession? session = null;

            try {
                // context
                session = new(this, transport, _bufferPool);
                RegisterSession(session);

                session.Connect();                                       // receive data
                await session.ProcessAsync().ConfigureAwait(false);      // handle data
            }
            catch (Exception ex) {
                _log.Warn("session error", ex);
            }
            finally {
                if (session != null) {
                    CloseSession(session);
                }
                else {
                    try { transport.Dispose(); } catch { }
                }
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
            if (Sessions.TryAdd(session.Id, session)) {
                Telemetry?.ActiveSessionIncrement();
                _log.Debug(() => $"create session {session.Id}");
            }
        }

        /// <summary>
        /// Close and Unregister Session
        /// </summary>
        /// <param name="session"></param>
        internal void CloseSession(HttpSession session) {
            if (Sessions.TryRemove(session.Id, out _)) {
                try {
                    session.Dispose();
                }
                catch { }
                finally {
                    _log.Debug(() => $"close session {session.Id}");
                    Telemetry?.ActiveSessionDecrement();
                }
            }
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

        /// <summary>
        /// Close all actives Sessions
        /// </summary>
        public void CloseAllSessions() {
            foreach (var session in Sessions.Values.ToArray()) {
                CloseSession(session);
            }
        }

        #region idle timeout

        /// <summary>
        /// Task for Idle Timeout
        /// </summary>
        private Task? _sessionTimeoutTask;

        /// <summary>
        /// Start Session Timeout Task
        /// </summary>
        /// <param name="token"></param>
        private void StartSessionTimeoutLoop(CancellationToken token) {
            if (Options.SessionTimeout == TimeSpan.MinValue) {
                return;
            }
            if (_sessionTimeoutTask != null) {
                return;
            }

            double periodSeconds = Math.Min(5, Options.SessionTimeout.TotalSeconds / 2);
            TimeSpan period = TimeSpan.FromSeconds(Math.Max(0.1, periodSeconds));

            _sessionTimeoutTask = Task.Run(async () => {
                try {
                    // small delay
                    await Task.Delay(period, token).ConfigureAwait(false);

                    while (!token.IsCancellationRequested) {
                        try {
                            CheckSessionTimeoutCore();
                        }
                        catch (Exception ex) {
                            _log.Warn("session check timeout error", ex);
                        }

                        await Task.Delay(period, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) {
                    // normal
                }
            }, CancellationToken.None);
        }

        /// <summary>
        /// Stop Session Timeout Task
        /// </summary>
        /// <returns></returns>
        private async Task StopSessionTimeoutLoopAsync() {
            Task? task = _sessionTimeoutTask;
            _sessionTimeoutTask = null;
            if (task != null) {
                try { await task.ConfigureAwait(false); }
                catch { }
            }
        }

        /// <summary>
        /// Close and Remove Session Timeout
        /// </summary>
        private void CheckSessionTimeoutCore() {
            if (State != SimpleWServerState.Started && State != SimpleWServerState.Reloading) {
                return;
            }

            long now = Environment.TickCount64;
            long timeoutMs = (long)Options.SessionTimeout.TotalMilliseconds;

            foreach (KeyValuePair<Guid, HttpSession> kvp in Sessions) {
                HttpSession session = kvp.Value;

                // Do not check the timeout when IsTransportOwned,
                // as responsibility is delegated to the underlying service
                // (e.g., WebSocketModule).
                if (session.IsTransportOwned) {
                    continue;
                }

                if (now - session.LastActivityTick > timeoutMs) {
                    _log.Debug(() => $"timeout session {session.Id}");
                    CloseSession(session);
                }
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
            ArgumentNullException.ThrowIfNull(jsonEngine);

            lock (_stateLock) {
                EnsureStateStopped("JsonEngine must be configured before starting the server.");
                JsonEngine = jsonEngine;
            }
            return this;
        }

        #endregion json engine

        #region telemetry

        /// <summary>
        /// Telemetry Options
        /// </summary>
        private readonly TelemetryOptions _telemetryOptions = new();

        /// <summary>
        /// Telemetry
        /// </summary>
        public Telemetry? Telemetry { get; private set; }

        /// <summary>
        /// Telemetry Status
        /// </summary>
        public bool IsTelemetryEnabled => Telemetry != null;

        /// <summary>
        /// Configure Telemetry
        /// </summary>
        /// <param name="configure"></param>
        /// <example>
        /// server.ConfigureTelemetry(options => {
        ///     options.Enabled = true;
        ///     options.IncludeStackTrace = true;
        /// });
        /// </example>
        /// <returns></returns>
        public SimpleWServer ConfigureTelemetry(Action<TelemetryOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            lock (_stateLock) {
                EnsureStateStopped("Telemetry must be configured before starting the server.");
                configure(_telemetryOptions);
            }
            return this;
        }

        #endregion telemetry

        #region principal

        /// <summary>
        /// Resolve the current principal for each request.
        /// Return null to use HttpPrincipal.Anonymous.
        /// </summary>
        public HttpPrincipalResolver? PrincipalResolver { get; private set; }

        /// <summary>
        /// Configure the principal resolver.
        /// Lazy fallback used to resolve the request principal on first access
        /// </summary>
        public SimpleWServer ConfigurePrincipalResolver(HttpPrincipalResolver resolver) {
            ArgumentNullException.ThrowIfNull(resolver);
            PrincipalResolver = resolver;
            return this;
        }

        #endregion principal

        #region client ip resolver

        /// <summary>
        /// Client IP Resolver
        /// </summary>
        internal Func<HttpSession, IPAddress?> ClientIpResolver { get; private set; } = (session) => {
            if (session.RemoteEndPoint is not IPEndPoint ep) {
                return null;
            }
            return ep.Address;
        };

        /// <summary>
        /// Configure the ClientIPResolver
        /// </summary>
        /// <param name="clientIPResolver"></param>
        /// <returns></returns>
        public SimpleWServer ConfigureClientIPResolver(Func<HttpSession, IPAddress?> clientIPResolver) {
            ClientIpResolver = clientIPResolver;
            return this;
        }

        #endregion client ip resolver

    }

    /// <summary>
    /// SimpleWSServer Options
    /// </summary>
    public sealed class SimpleWSServerOptions {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<SimpleWSServerOptions>();

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
        /// Maximum time allowed to receive a complete HTTP request header block
        /// Protects against slowloris attacks that send headers very slowly.
        /// Use <see cref="TimeSpan.MinValue"/> to disable this protection.
        /// Default recommended value: 5 seconds.
        /// </summary>
        public TimeSpan RequestHeadersTimeout { get; set; } = TimeSpan.FromSeconds(5);

        internal long RequestHeadersTimeoutMs { get; private set; }

        /// <summary>
        /// Minimum sustained body receive rate, in bytes per second,
        /// required after <see cref="RequestBodyGracePeriod"/> has elapsed.
        /// If the request body progresses slower than this rate, the connection
        /// is considered too slow and will be closed.
        /// Set 0 to disable this protection.
        /// Default: 256 bytes/s.
        /// </summary>
        public int MinRequestBodyDataRateBytesPerSecond { get; set; } = 256;

        /// <summary>
        /// Grace period applied before enforcing
        /// <see cref="MinRequestBodyDataRateBytesPerSecond"/>.
        /// This gives the client a short time window before the minimum body
        /// receive rate check begins.
        /// Set <see cref="TimeSpan.Zero"/> for no grace period.
        /// Default: 5 seconds.
        /// </summary>
        public TimeSpan RequestBodyGracePeriod { get; set; } = TimeSpan.FromSeconds(5);

        internal long RequestBodyGracePeriodMs { get; private set; }

        #endregion security


        #region session

        /// <summary>
        /// Idle timeout (if no data received during timeout, then close connection)
        /// Set TimeSpan.MinValue to disable.
        /// </summary>
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        #endregion session

        #region handler

        /// <summary>
        /// When a RequestAborted cancellation token is get inside an async handler,
        /// a task is run periodically to poll the underlying socket
        /// to detect when the client disconnects.
        /// Default polling every 250ms (TimeSpan.Zero to disable)
        /// </summary>
        public TimeSpan SocketDisconnectPollInterval { get; set; } = TimeSpan.FromMilliseconds(250);

        #endregion handler

        /// <summary>
        /// Check Properties and return
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public SimpleWSServerOptions ValidateAndNormalize() {

            // basic ranges
            if (MaxRequestHeaderSize <= 0) {
                ArgumentOutOfRangeException ex = new(nameof(MaxRequestHeaderSize), "Must be > 0.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (MaxRequestBodySize <= 0) {
                ArgumentOutOfRangeException ex = new(nameof(MaxRequestBodySize), "Must be > 0.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (RequestHeadersTimeout != TimeSpan.MinValue && RequestHeadersTimeout <= TimeSpan.Zero) {
                ArgumentOutOfRangeException ex = new(nameof(RequestHeadersTimeout), "Must be > 0 or TimeSpan.MinValue to disable.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (MinRequestBodyDataRateBytesPerSecond < 0) {
                ArgumentOutOfRangeException ex = new(nameof(MinRequestBodyDataRateBytesPerSecond), "Must be >= 0.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (RequestBodyGracePeriod != TimeSpan.MinValue && RequestBodyGracePeriod <= TimeSpan.Zero) {
                ArgumentOutOfRangeException ex = new(nameof(RequestBodyGracePeriod), "Must be = 0 or TimeSpan.MinValue to disable.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (SessionTimeout != TimeSpan.MinValue && SessionTimeout < TimeSpan.Zero) {
                ArgumentOutOfRangeException ex = new(nameof(SessionTimeout), "Must be >= 0 or TimeSpan.MinValue to disable.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }

            // optimize properties
            RequestHeadersTimeoutMs = RequestHeadersTimeout == TimeSpan.MinValue ? 0 : (long)RequestHeadersTimeout.TotalMilliseconds;
            RequestBodyGracePeriodMs = RequestBodyGracePeriod == TimeSpan.MinValue ? 0 : (long)RequestBodyGracePeriod.TotalMilliseconds;

            return this;
        }

    }

    /// <summary>
    /// SimpleW server lifecycle state.
    /// </summary>
    public enum SimpleWServerState {

        /// <summary>
        /// The server is not running.
        /// </summary>
        Stopped,

        /// <summary>
        /// The server is starting its listener and runtime services.
        /// </summary>
        Starting,

        /// <summary>
        /// The server is running.
        /// </summary>
        Started,

        /// <summary>
        /// The server is replacing its listener.
        /// </summary>
        Reloading,

        /// <summary>
        /// The server is stopping.
        /// </summary>
        Stopping,

        /// <summary>
        /// A lifecycle operation failed and cleanup is required.
        /// </summary>
        Faulted

    }

}
