using System.Diagnostics;
using System.Net;
using SimpleW.Observability;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Manages frontend startup, readiness and shutdown for one SimpleW development server.
    /// </summary>
    internal sealed class DevelopmentServer {
        private readonly SimpleWServer _server;
        private readonly NpmOptions _options;
        private readonly ApiAddress _api;
        private readonly string _source;
        private string? _origin;
        private CancellationTokenSource? _cancellation;
        private Task? _lifetime;

        /// <summary>
        /// Resolves the frontend directory and validates script settings and timeouts.
        /// </summary>
        /// <param name="server">Server whose listener supplies the default API origin.</param>
        /// <param name="options">Frontend configuration and loggers used throughout supervision.</param>
        /// <param name="api">Validated API address.</param>
        public DevelopmentServer(SimpleWServer server, NpmOptions options, ApiAddress api) {
            _server = server;
            _options = options;
            _api = api;
            _source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.SourcePath, AppContext.BaseDirectory));
            if (options.StartupTimeout <= TimeSpan.Zero || options.InstallTimeout <= TimeSpan.Zero) {
                throw new ArgumentException("Npm timeouts must be positive.");
            }
            if (string.IsNullOrWhiteSpace(options.Script) || options.Script.StartsWith('-')) {
                throw new ArgumentException("Script must name an npm script.");
            }
        }

        /// <summary>
        /// Starts supervision in the background unless a supervision task already exists.
        /// </summary>
        public void Start() {
            if (_lifetime != null) {
                return;
            }
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            _lifetime = Task.Run(() => RunAsync(token), CancellationToken.None);
        }

        /// <summary>
        /// Cancels supervision, waits for owned process cleanup and releases the lifetime state.
        /// </summary>
        /// <returns>A task that completes when supervision and cleanup have finished.</returns>
        public async Task StopAsync() {
            Volatile.Write(ref _origin, null);
            if (_lifetime == null) {
                return;
            }
            _cancellation!.Cancel();
            try { await _lifetime.ConfigureAwait(false); }
            finally {
                _lifetime = null;
                _cancellation.Dispose();
                _cancellation = null;
            }
        }

        /// <summary>
        /// Allows only the discovered frontend origin while its managed process is ready.
        /// </summary>
        /// <param name="origin">Origin supplied by the browser.</param>
        /// <returns>True when it matches the active frontend origin exactly, ignoring case.</returns>
        public bool IsOriginAllowed(string origin) => string.Equals(Volatile.Read(ref _origin), origin, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Runs a managed frontend and discovers its address, logging failures without stopping the backend.
        /// </summary>
        /// <param name="token">Cancellation requested when the SimpleW server stops.</param>
        /// <returns>A task representing the frontend supervision lifetime.</returns>
        private async Task RunAsync(CancellationToken token) {
            try {
                using HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) {
                    Timeout = TimeSpan.FromSeconds(2)
                };
                string apiUrl = _api.Resolve(_server);
                if (!File.Exists(Path.Combine(_source, "package.json"))) {
                    throw new FileNotFoundException($"Client package.json missing in '{_source}'. Configure SourcePath.");
                }
                NodeToolchain toolchain = await NodeToolchain.FindAsync(_options, token).ConfigureAwait(false);
                Dictionary<string, string> environment = new() {
                    ["VITE_SIMPLEW_API_URL"] = apiUrl,
                    ["NODE_ENV"] = "development"
                };
                if (DependenciesMissing()) {
                    if (!_options.InstallDependencies) {
                        throw new InvalidOperationException($"Client dependencies are missing in '{_source}'. Enable InstallDependencies or install them separately.");
                    }
                    string install = File.Exists(Path.Combine(_source, "package-lock.json")) ? "ci" : "install";
                    Log($"npm {install} --include=dev in {_source}");
                    using CancellationTokenSource installTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    installTimeout.CancelAfter(_options.InstallTimeout);
                    await using NpmProcess installer = NpmProcess.Start(toolchain.Node, toolchain.NpmCli, _source,
                        [install, "--include=dev"], environment, _options.ClientLogger);
                    try { await installer.WaitAsync(installTimeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                        throw new TimeoutException($"npm installation exceeded {_options.InstallTimeout}.");
                    }
                    if (installer.ExitCode != 0) {
                        throw new InvalidOperationException($"npm {install} failed with exit code {installer.ExitCode}.");
                    }
                }
                token.ThrowIfCancellationRequested();
                List<string> arguments = ["run", _options.Script];
                if (_options.ScriptArguments.Count != 0) {
                    arguments.Add("--");
                    arguments.AddRange(_options.ScriptArguments);
                }
                Log($"npm run {_options.Script} in {_source}; API: {apiUrl}");
                FrontendAddressDiscovery discovery = new();
                await using NpmProcess client = NpmProcess.Start(
                    toolchain.Node, toolchain.NpmCli, _source,
                    arguments, environment, _options.ClientLogger, discovery.Observe
                );
                try {
                    Uri url = await WaitUntilReadyAsync(http, client, discovery, token).ConfigureAwait(false);
                    Volatile.Write(ref _origin, url.GetLeftPart(UriPartial.Authority));
                    token.ThrowIfCancellationRequested();
                    Log($"Frontend ready: {url}; direct API requests: {apiUrl}");
                    OpenBrowser(url);
                    await client.WaitAsync(token).ConfigureAwait(false);
                    if (!token.IsCancellationRequested) {
                        throw new InvalidOperationException($"Client stopped unexpectedly (exit code {client.ExitCode}). Restart the backend to start it again.");
                    }
                }
                finally {
                    Volatile.Write(ref _origin, null);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) {
                Log("Client supervision stopped.");
            }
            catch (Exception ex) {
                Log("Client supervision failed. Backend remains available.", LogLevel.Error, ex);
            }
        }

        /// <summary>
        /// Discovers announced URLs and probes them until one responds, the process exits or startup times out.
        /// </summary>
        /// <param name="http">HTTP client used to probe announced frontend URLs.</param>
        /// <param name="client">Managed process to watch for early exit.</param>
        /// <param name="discovery">Collector attached before the development script starts.</param>
        /// <param name="token">Cancellation requested when supervision stops.</param>
        /// <returns>The announced URL that returned an HTTP 2xx response, including its base path.</returns>
        private async Task<Uri> WaitUntilReadyAsync(HttpClient http, NpmProcess client, FrontendAddressDiscovery discovery, CancellationToken token) {
            using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(_options.StartupTimeout);
            try {
                while (true) {
                    ThrowIfExited(client);
                    Uri[] candidates = discovery.Snapshot();
                    HttpStatusCode?[] statuses = await Task.WhenAll(candidates.Select(url => ProbeAsync(http, url, startup.Token))).ConfigureAwait(false);
                    ThrowIfExited(client);
                    for (int i = 0; i < candidates.Length; i++) {
                        if (IsSuccessful(statuses[i])) return candidates[i];
                    }
                    await Task.Delay(250, startup.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                Uri[] candidates = discovery.Snapshot();
                throw new TimeoutException(candidates.Length == 0
                    ? $"No local frontend URL was announced within {_options.StartupTimeout}. The development script must print its HTTP(S) address, for example 'Local: http://localhost:<port>/'. Check the frontend logs and ensure startup output is enabled."
                    : $"Frontend URLs were detected but none returned HTTP 2xx within {_options.StartupTimeout}: {string.Join(", ", candidates.Select(url => url.AbsoluteUri))}. Check the frontend logs and HTTPS certificate trust if applicable.");
            }
        }

        /// <summary>
        /// Reports a managed process that exited before the frontend became ready.
        /// </summary>
        /// <param name="client">Process to inspect.</param>
        private static void ThrowIfExited(NpmProcess client) {
            if (client.HasExited) {
                throw new InvalidOperationException($"npm exited before the frontend was ready (code {client.ExitCode}).");
            }
        }

        /// <summary>
        /// Determines whether a probe returned an HTTP 2xx status.
        /// </summary>
        /// <param name="status">Response status, or null when no response was received.</param>
        /// <returns>True when the frontend returned a successful status.</returns>
        private static bool IsSuccessful(HttpStatusCode? status) => status.HasValue && (int)status.Value is >= 200 and <= 299;

        /// <summary>
        /// Requests an announced frontend URL without reading its response body.
        /// </summary>
        /// <param name="http">HTTP client used for the readiness request.</param>
        /// <param name="url">Announced frontend URL, including any base path.</param>
        /// <param name="token">Cancellation token for the request; caller cancellation is propagated.</param>
        /// <returns>The response status, or null for an HTTP connection failure or a request timeout.</returns>
        private static async Task<HttpStatusCode?> ProbeAsync(HttpClient http, Uri url, CancellationToken token) {
            try {
                using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                return response.StatusCode;
            }
            catch (System.Net.Http.HttpRequestException) { return null; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        }

        /// <summary>
        /// Checks the client's node_modules directory for each declared dependency and development dependency.
        /// </summary>
        /// <returns>True if any direct dependency lacks a package.json file; installed versions are not compared.</returns>
        private bool DependenciesMissing() {
            using System.Text.Json.JsonDocument package = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_source, "package.json")));
            foreach (string group in new[] { "dependencies", "devDependencies" }) {
                if (!package.RootElement.TryGetProperty(group, out var dependencies)) {
                    continue;
                }
                foreach (var dependency in dependencies.EnumerateObject()) {
                    if (!File.Exists(Path.Combine(_source, "node_modules", dependency.Name, "package.json"))) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Opens the frontend in the default browser when enabled, logging launch failures as warnings.
        /// </summary>
        /// <param name="url">Ready frontend URL, including any base path.</param>
        private void OpenBrowser(Uri url) {
            if (!_options.OpenBrowser) {
                return;
            }
            try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Log($"Cannot open the browser. Open {url} manually.", LogLevel.Warning, ex); }
        }

        /// <summary>
        /// Writes a supervision message while isolating logger failures from process cleanup.
        /// </summary>
        /// <param name="message">Message describing the supervision event.</param>
        /// <param name="level">Severity; Error and Warning are handled explicitly, otherwise Information is used.</param>
        /// <param name="exception">Optional exception attached to an error or warning.</param>
        private void Log(string message, LogLevel level = LogLevel.Information, Exception? exception = null) {
            try {
                if (level == LogLevel.Error) {
                    if (exception != null) {
                        _options.ServerLogger.Error(message, exception);
                    }
                    else {
                        _options.ServerLogger.Error(message);
                    }
                }
                else if (level == LogLevel.Warning) {
                    if (exception != null) {
                        _options.ServerLogger.Warn(message, exception);
                    }
                    else {
                        _options.ServerLogger.Warn(message);
                    }
                }
                else {
                    _options.ServerLogger.Info(message);
                }
            }
            catch { /* Consumer loggers must never interrupt process cleanup. */ }
        }

    }

}
