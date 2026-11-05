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
        private readonly string _source;
        private readonly Uri _url;
        private CancellationTokenSource? _cancellation;
        private Task? _lifetime;

        /// <summary>
        /// Resolves the frontend directory and validates development URLs, script settings and timeouts.
        /// </summary>
        /// <param name="server">Server whose listener supplies the default API origin.</param>
        /// <param name="options">Frontend configuration and loggers used throughout supervision.</param>
        public DevelopmentServer(SimpleWServer server, NpmOptions options) {
            _server = server;
            _options = options;
            _source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.SourcePath, AppContext.BaseDirectory));
            _url = ValidateUrl(options.DevelopmentUrl);
            if (!_url.IsLoopback) {
                throw new ArgumentException("DevelopmentUrl must use a loopback host.");
            }
            if (options.StartupTimeout <= TimeSpan.Zero || options.InstallTimeout <= TimeSpan.Zero) {
                throw new ArgumentException("Npm timeouts must be positive.");
            }
            if (string.IsNullOrWhiteSpace(options.Script) || options.Script.StartsWith('-')) {
                throw new ArgumentException("Script must name an npm script.");
            }
            string prefix = options.ApiPrefix;
            if (!prefix.StartsWith('/') || prefix.StartsWith("//", StringComparison.Ordinal)
                || prefix.TrimEnd('/').Length < 2 || prefix.IndexOfAny(['?', '#', '\\']) >= 0
            ) {
                throw new ArgumentException("ApiPrefix must be an absolute URL path such as /api.");
            }
            if (options.BackendUrl != null) {
                ValidateUrl(options.BackendUrl);
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
        /// Runs a managed frontend or monitors an external one, logging failures without stopping the backend.
        /// </summary>
        /// <param name="token">Cancellation requested when the SimpleW server stops.</param>
        /// <returns>A task representing the frontend supervision lifetime.</returns>
        private async Task RunAsync(CancellationToken token) {
            try {
                using HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) {
                    Timeout = TimeSpan.FromSeconds(2)
                };
                string apiUrl = BackendUrl() + _options.ApiPrefix.TrimEnd('/');
                if (!_options.LaunchDevelopmentServer) {
                    Log($"Waiting for external frontend at {_url}. Configure VITE_SIMPLEW_API_URL={apiUrl} in its environment.");
                    await WaitUntilReadyAsync(http, null, token).ConfigureAwait(false);
                    OpenBrowser();
                    while (true) {
                        await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                        if (!IsSuccessful(await ProbeAsync(http, token).ConfigureAwait(false))) {
                            throw new InvalidOperationException("The externally managed frontend is no longer available. Its process has not been modified.");
                        }
                    }
                }

                if (!File.Exists(Path.Combine(_source, "package.json"))) {
                    throw new FileNotFoundException($"Client package.json missing in '{_source}'. Configure SourcePath.");
                }
                if (await ProbeAsync(http, token).ConfigureAwait(false) != null) {
                    throw new InvalidOperationException($"A server already responds at {_url}. Stop it or set LaunchDevelopmentServer=false to use an externally managed frontend.");
                }
                // A probe timeout does not prove a port is busy. The script must fail on bind conflicts
                // (Vite: strictPort=true) instead of switching silently to another port.
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
                await using NpmProcess client = NpmProcess.Start(
                    toolchain.Node, toolchain.NpmCli, _source,
                    arguments, environment, _options.ClientLogger
                );
                await WaitUntilReadyAsync(http, client, token).ConfigureAwait(false);
                Log($"Frontend ready: {_url}; direct API requests: {apiUrl}");
                OpenBrowser();
                await client.WaitAsync(token).ConfigureAwait(false);
                if (!token.IsCancellationRequested) {
                    throw new InvalidOperationException($"Client stopped unexpectedly (exit code {client.ExitCode}). Restart the backend to start it again.");
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
        /// Polls the frontend until it responds successfully, the managed process exits or startup times out.
        /// </summary>
        /// <param name="http">HTTP client used to probe the configured frontend origin.</param>
        /// <param name="client">Managed process to watch for early exit, or null for an external frontend.</param>
        /// <param name="token">Cancellation requested when supervision stops.</param>
        /// <returns>A task that completes when the frontend returns an HTTP 2xx response.</returns>
        private async Task WaitUntilReadyAsync(HttpClient http, NpmProcess? client, CancellationToken token) {
            using CancellationTokenSource startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(_options.StartupTimeout);
            try {
                while (true) {
                    ThrowIfExited(client);
                    bool ready = IsSuccessful(await ProbeAsync(http, startup.Token).ConfigureAwait(false));
                    ThrowIfExited(client);
                    if (ready) {
                        return;
                    }
                    await Task.Delay(250, startup.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                throw new TimeoutException($"Frontend did not become ready at {_url} within {_options.StartupTimeout}. Check the script, host and port.");
            }
        }

        /// <summary>
        /// Reports a managed process that exited before the frontend became ready.
        /// </summary>
        /// <param name="client">Process to inspect, or null when the frontend is externally managed.</param>
        private static void ThrowIfExited(NpmProcess? client) {
            if (client?.HasExited == true) {
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
        /// Requests the frontend root without reading its response body.
        /// </summary>
        /// <param name="http">HTTP client used for the readiness request.</param>
        /// <param name="token">Cancellation token for the request; caller cancellation is propagated.</param>
        /// <returns>The response status, or null for an HTTP connection failure or a request timeout.</returns>
        private async Task<HttpStatusCode?> ProbeAsync(HttpClient http, CancellationToken token) {
            try {
                using HttpResponseMessage response = await http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                return response.StatusCode;
            }
            catch (HttpRequestException) { return null; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        }

        /// <summary>
        /// Resolves the API origin from the override or active listener, replacing wildcard addresses with loopback.
        /// </summary>
        /// <returns>The HTTP(S) backend origin without the API prefix.</returns>
        private string BackendUrl() {
            if (_options.BackendUrl != null)
                return ValidateUrl(_options.BackendUrl).GetLeftPart(UriPartial.Authority);
            IPAddress address = _server.Address ?? throw new InvalidOperationException("Set BackendUrl when using a Unix socket.");
            if (address.Equals(IPAddress.Any)) {
                address = IPAddress.Loopback;
            }
            if (address.Equals(IPAddress.IPv6Any)) {
                address = IPAddress.IPv6Loopback;
            }
            return new UriBuilder(_server.Engine.IsEncrypted ? "https" : "http", address.ToString(), _server.Port).Uri.GetLeftPart(UriPartial.Authority);
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
        /// Validates an HTTP(S) origin without credentials, a subpath, a query or a fragment.
        /// </summary>
        /// <param name="value">Absolute origin to parse.</param>
        /// <returns>The validated URI.</returns>
        private static Uri ValidateUrl(string value) {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
                || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0
            ) {
                throw new ArgumentException($"Expected an HTTP(S) origin without credentials or a path, got '{value}'.");
            }
            return uri;
        }

        /// <summary>
        /// Opens the frontend in the default browser when enabled, logging launch failures as warnings.
        /// </summary>
        private void OpenBrowser() {
            if (!_options.OpenBrowser) {
                return;
            }
            try { Process.Start(new ProcessStartInfo(_url.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { Log($"Cannot open the browser. Open {_url} manually.", LogLevel.Warning, ex); }
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
