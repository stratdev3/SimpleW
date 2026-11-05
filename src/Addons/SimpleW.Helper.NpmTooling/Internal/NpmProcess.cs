using System.Diagnostics;
using SimpleW.Observability;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Owns an npm supervisor process, captures its output and cleans up its process tree.
    /// </summary>
    internal sealed class NpmProcess : IAsyncDisposable {
        private static readonly string Supervisor = ReadSupervisor();
        private readonly Process _process;
        private readonly WindowsJob? _job;
        private readonly Task _output;
        private readonly Task _error;

        /// <summary>
        /// Attaches output readers to an already started supervisor process.
        /// </summary>
        /// <param name="process">Supervisor with redirected stdout and stderr.</param>
        /// <param name="job">Windows process ownership handle, or null on other platforms.</param>
        /// <param name="logger">Logger receiving npm output.</param>
        /// <param name="observeOutput">Optional observer of stdout and stderr, attached before npm starts.</param>
        private NpmProcess(Process process, WindowsJob? job, ILogger logger, Action<string>? observeOutput) {
            _process = process;
            _job = job;
            _output = PumpAsync(process.StandardOutput, logger, isError: false, observeOutput);
            _error = PumpAsync(process.StandardError, logger, isError: true, observeOutput);
        }

        /// <summary>
        /// Gets whether the supervisor process has exited.
        /// </summary>
        public bool HasExited => _process.HasExited;

        /// <summary>
        /// Gets the supervisor exit code after it has exited.
        /// </summary>
        public int ExitCode => _process.ExitCode;

        /// <summary>
        /// Waits for the supervisor to exit; canceling the wait does not dispose the process.
        /// </summary>
        /// <param name="token">Cancellation token for the wait.</param>
        /// <returns>A task that completes when the supervisor exits.</returns>
        public Task WaitAsync(CancellationToken token) => _process.WaitForExitAsync(token);

        /// <summary>
        /// Starts the embedded supervisor and allows it to launch npm after process ownership is established.
        /// </summary>
        /// <param name="node">Node executable used to run the supervisor.</param>
        /// <param name="npmCli">npm-cli.js file passed to the supervisor.</param>
        /// <param name="directory">Working directory of the frontend project.</param>
        /// <param name="arguments">Separate npm command arguments.</param>
        /// <param name="environment">Environment variables applied to the child process.</param>
        /// <param name="logger">Logger receiving npm stdout and stderr.</param>
        /// <param name="observeOutput">Optional observer called for each nonempty output line.</param>
        /// <returns>The owned process wrapper, which must be disposed to stop remaining descendants.</returns>
        public static NpmProcess Start(string node, string npmCli, string directory, IEnumerable<string> arguments,
            IReadOnlyDictionary<string, string> environment, ILogger logger, Action<string>? observeOutput = null) {
            ProcessStartInfo info = new(node) {
                WorkingDirectory = directory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("--eval");
            info.ArgumentList.Add(Supervisor);
            info.ArgumentList.Add("--");
            info.ArgumentList.Add(npmCli);
            foreach (string argument in arguments) {
                info.ArgumentList.Add(argument);
            }
            foreach (var entry in environment) {
                info.Environment[entry.Key] = entry.Value;
            }
            Process process = new() { StartInfo = info };
            WindowsJob? job = null;
            try {
                process.Start();
                // The supervisor waits for this handshake before spawning npm, eliminating the job assignment race.
                if (OperatingSystem.IsWindows()) {
                    job = new WindowsJob(process);
                }
                NpmProcess result = new(process, job, logger, observeOutput);
                process.StandardInput.WriteLine("start");
                process.StandardInput.Flush();
                return result;
            }
            catch {
                job?.Dispose();
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
                process.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Requests supervisor shutdown, forcibly stops remaining owned processes if needed and releases resources.
        /// </summary>
        /// <returns>A task that completes after process cleanup and bounded output draining.</returns>
        public async ValueTask DisposeAsync() {
            try {
                if (!_process.HasExited) {
                    try {
                        await _process.StandardInput.WriteLineAsync("stop").ConfigureAwait(false);
                        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                        _process.StandardInput.Close();
                    }
                    catch (IOException) { }
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
                    try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) {
                        if (!_process.HasExited) {
                            _process.Kill(entireProcessTree: true);
                        }
                    }
                }
            }
            finally {
                _job?.Dispose(); // Also kills descendants left behind after npm itself exited.
                try { await Task.WhenAll(_output, _error).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
                catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException) { }
                _process.Dispose();
            }
        }

        /// <summary>
        /// Drains nonempty output lines to the logger, continuing even when the logger throws.
        /// </summary>
        /// <param name="reader">Redirected stdout or stderr reader.</param>
        /// <param name="logger">Destination logger for process output.</param>
        /// <param name="isError">True logs stderr as warnings; false logs stdout as information.</param>
        /// <param name="observeOutput">Optional observer, independent of consumer logging.</param>
        /// <returns>A task that completes when the output stream closes.</returns>
        private static async Task PumpAsync(StreamReader reader, ILogger logger, bool isError, Action<string>? observeOutput) {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try { observeOutput?.Invoke(line); }
                catch { /* Keep draining output if an observer fails. */ }
                try {
                    // stderr also carries npm/Vite warnings; process failures are logged by the supervisor.
                    if (isError)
                        logger.Warn(line);
                    else
                        logger.Info(line);
                }
                catch { /* Keep draining output even if a consumer logger throws. */ }
            }
        }

        /// <summary>
        /// Reads the embedded JavaScript supervisor shipped in the helper assembly.
        /// </summary>
        /// <returns>The source passed to Node through its eval argument.</returns>
        private static string ReadSupervisor() {
            using Stream stream = typeof(NpmProcess).Assembly.GetManifestResourceStream("SimpleW.Helper.NpmTooling.supervisor.cjs")
                                  ?? throw new InvalidOperationException("Missing embedded npm supervisor.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

    }

}
