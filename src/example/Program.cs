using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using SimpleW;
using SimpleW.Observability;
#if NET10_0
using SimpleW.Engine.Ioxide;
#endif


namespace example {

    /// <summary>
    /// Example Program
    /// perf http : bombardier -c 200 -d 10s http://127.0.0.1:8080/api/test/hello
    /// perf https : bombardier -c 200 -d 10s -k https://127.0.0.1:8080/api/test/hello
    /// perf jwt : bombardier -c 200 -d 10s -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjaGVmIiwiaWF0IjoxNzY3NDQxOTA4LCJleHAiOjE3Njc0NDkxMDgsInJvbGVzIjpbImJvc3MiXX0.qv9TnM7RKM1zu_QcPoTz_pmXMDzHT-OnP6xjs4nMgS4" http://127.0.0.1:8080/api/jwt/decode
    /// </summary>
    internal static class Program {

        /// <summary>
        /// Force Arguments
        /// </summary>
        private static string[] _args = [
            //"hello",
            //"--port", "8080" // already default
            //"--log",
            //"--browser"
        ];

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        public static async Task<int> Main(string[] args) {
            string[] effectiveArgs = _args.Length > 0 ? _args : args;

            if (effectiveArgs.Length == 0) {
                PrintScenarioList();
                return 0;
            }

            bool listRequested = effectiveArgs.Any(static arg => string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase));

            if (listRequested && (effectiveArgs.Length != 1 || !string.Equals(effectiveArgs[0], "--list", StringComparison.OrdinalIgnoreCase))) {
                ExampleConsole.Error("Option '--list' must be used on its own.");
                return 2;
            }
            if (listRequested) {
                PrintScenarioList();
                return 0;
            }

            bool hasExplicitScenario = !effectiveArgs[0].StartsWith("--", StringComparison.Ordinal);
            if (!hasExplicitScenario && effectiveArgs.Length == 1 && string.Equals(effectiveArgs[0], "--help", StringComparison.OrdinalIgnoreCase)) {
                PrintScenarioList();
                return 0;
            }

            string scenarioName = hasExplicitScenario ? effectiveArgs[0] : "hello";
            string[] optionArgs = hasExplicitScenario ? effectiveArgs[1..] : effectiveArgs;

            if (!Scenarios.TryGetValue(scenarioName, out IScenario? scenario)) {
                ExampleConsole.Error($"Unknown scenario '{scenarioName}'.");
                PrintScenarioList();
                return 2;
            }

            try {
                ExampleArguments arguments = ExampleArguments.Parse(optionArgs);
                if (arguments.HasFlag("help")) {
                    PrintScenarioHelp(scenario);
                    return 0;
                }

                bool requestLoggingEnabled = arguments.HasFlag("log");
                bool browserEnabled = arguments.HasFlag("browser");
                bool ioxideEnabled = arguments.HasFlag("ioxide");
                arguments.ConfigureBrowser(browserEnabled, scenario.BrowserPath);
                arguments.ConfigureIoxide(ioxideEnabled);
                using IDisposable? requestLogging = requestLoggingEnabled ? ExampleRequestLogging.Enable() : null;
                using CancellationTokenSource cancellation = new();
                ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => {
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                };

                Console.CancelKeyPress += cancelHandler;
                try {
                    ExampleConsole.Scenario(scenario.Name, scenario.Description);
                    if (requestLoggingEnabled) {
                        ExampleConsole.Info("HTTP request logging is enabled.");
                        ExampleConsole.BlankLine();
                    }
                    await scenario.RunAsync(arguments, cancellation.Token).ConfigureAwait(false);
                    return 0;
                }
                finally {
                    Console.CancelKeyPress -= cancelHandler;
                }
            }
            catch (ExampleCliException ex) {
                ExampleConsole.Error(ex.Message);
                ExampleConsole.BlankLine(writeToError: true);
                PrintScenarioHelp(scenario);
                return 2;
            }
            catch (OperationCanceledException) {
                return 0;
            }
            catch (Exception ex) {
                ExampleConsole.Error($"Scenario '{scenario.Name}' failed: {ex.Message}");
                return 1;
            }
        }

        #region scenarios

        private static readonly IReadOnlyDictionary<string, IScenario> Scenarios = CreateScenarios();

        private static IReadOnlyDictionary<string, IScenario> CreateScenarios() {
            IScenario[] scenarios = [
                new HelloScenario(),
            new HttpArenaScenario(),
            new RequestScenario(),
            new NewtonsoftJsonScenario(),
            new StaticFilesScenario(),
            new UploadScenario(),
            new WebSocketScenario(),
            new ServerSentEventsScenario(),
            new HttpsScenario(),
            new LetsEncryptScenario(),
            new OpenIdScenario(),
            new LatencyScenario(),
            new FirewallScenario(),
            new ChaosScenario(),
            new RazorScenario(),
            new SwaggerScenario(),
            new ControllersScenario(),
            new TelemetryScenario()
            ];

            return scenarios.ToDictionary(static scenario => scenario.Name, StringComparer.OrdinalIgnoreCase);
        }

        #endregion scenarios

        #region help

        private static void PrintScenarioList() {
            ExampleConsole.Section("Usage");
            ExampleConsole.Command("example <scenario> [--port 8080] [--log] [--browser] [--ioxide] [options]");
            ExampleConsole.Command("example <scenario> --help");
            ExampleConsole.Command("example --list");
            ExampleConsole.BlankLine();
            ExampleConsole.Section("Scenarios");
            foreach (IScenario scenario in Scenarios.Values.OrderBy(static scenario => scenario.Name)) {
                ExampleConsole.Item(scenario.Name, scenario.Description);
            }
            ExampleConsole.BlankLine();
            ExampleConsole.Section("Examples");
            ExampleConsole.Command("example hello");
            ExampleConsole.Command("example hello --log");
            ExampleConsole.Command("example hello --ioxide --log");
            ExampleConsole.Command("example swagger --browser");
            ExampleConsole.Command("example websocket --port 8081");
            ExampleConsole.Command("example websocket --help");
            ExampleConsole.BlankLine();
            ExampleConsole.Section("Forced arguments");
            ExampleConsole.Command("_args = [\"websocket\", \"--port\", \"8081\"];");
            ExampleConsole.Muted("  Leave _args empty to use command-line arguments.");
        }

        private static void PrintScenarioHelp(IScenario scenario) {
            ExampleConsole.Section("Usage");
            ExampleConsole.Command($"example {scenario.Name} [--port 8080] [--log] [--browser] [--ioxide]{scenario.Usage}");
            ExampleConsole.BlankLine();
            ExampleConsole.Section("Scenario");
            ExampleConsole.Item(scenario.Name, scenario.Description);
        }

        #endregion help

    }

    #region ExampleServer

    internal static class ExampleServer {

        public static SimpleWServer Create(ExampleArguments arguments) {
            SimpleWServer server = CreateBase(arguments);
#if NET10_0
            if (arguments.IoxideEnabled) {
                server.UseIoxideEngine();
            }
#endif
            return server;
        }

#if NET10_0
        public static SimpleWServer CreateIoxide(ExampleArguments arguments, Action<IoxideEngineOptions> configure) {
            if (!arguments.IoxideEnabled) {
                throw new InvalidOperationException("The Ioxide server factory requires the global '--ioxide' option.");
            }

            SimpleWServer server = CreateBase(arguments);
            server.UseIoxideEngine(configure);
            return server;
        }
#endif

        private static SimpleWServer CreateBase(ExampleArguments arguments) {
            SimpleWServer server = new(IPAddress.Any, arguments.Port);
            server.Configure(options => {
                options.SessionTimeout = TimeSpan.FromMinutes(10);
            });
            return server;
        }

        public static async Task RunAsync(SimpleWServer server, string scheme, ExampleArguments arguments, CancellationToken cancellationToken) {
            server.OnStarted(started => {
                ExampleConsole.Success($"Listening on {scheme}://localhost:{started.Port}/");
                ExampleConsole.Info($"Network engine: {started.Engine.Name}.");

                RouteInfo[] routes = started.Router.Routes
                                            .OrderBy(static route => route.Host ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                                            .ThenBy(static route => route.Path, StringComparer.Ordinal)
                                            .ThenBy(static route => route.Method, StringComparer.OrdinalIgnoreCase)
                                            .ToArray();

                ExampleConsole.BlankLine();
                ExampleConsole.Section("Available routes");
                if (routes.Length == 0) {
                    ExampleConsole.Muted("  (none)");
                }
                else {
                    foreach (RouteInfo route in routes) {
                        ExampleConsole.Route(route);
                    }
                }

                if (arguments.BrowserEnabled) {
                    OpenBrowser(scheme, started.Port, arguments.BrowserPath);
                }

                ExampleConsole.BlankLine();
                ExampleConsole.Info("Press Ctrl+C to stop.");
            });
            await server.RunAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void OpenBrowser(string scheme, int port, string browserPath) {
            Uri baseUri = new($"{scheme}://localhost:{port}/", UriKind.Absolute);
            Uri browserUri = new(baseUri, browserPath);

            try {
                ExampleConsole.Info($"Opening browser: {browserUri.AbsoluteUri}");
                _ = Process.Start(new ProcessStartInfo {
                    FileName = browserUri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) {
                ExampleConsole.Warning($"Unable to open the browser at '{browserUri.AbsoluteUri}': {ex.Message}", writeToError: true);
            }
        }
    }

    #endregion ExampleServer

    #region ExampleRequestLogging

    internal sealed class ExampleRequestLogging : IDisposable {

        private static readonly string HttpSessionSource = typeof(HttpSession).FullName ?? nameof(HttpSession);

        private readonly Action<LogEntry>? _previousSink;

        private readonly LogLevel _previousMinimumLevel;

        private readonly Action<LogEntry> _installedSink;

        private bool _disposed;

        private ExampleRequestLogging() {
            _previousSink = Log.Sink;
            _previousMinimumLevel = Log.MinimumLevel;
            _installedSink = WriteLogEntry;

            LogLevel minimumLevel = (int)_previousMinimumLevel < (int)LogLevel.Information
                                      ? _previousMinimumLevel
                                      : LogLevel.Information;
            Log.SetSink(_installedSink, minimumLevel);
        }

        public static IDisposable Enable() => new ExampleRequestLogging();

        private void WriteLogEntry(LogEntry entry) {
            if (string.Equals(entry.Source, HttpSessionSource, StringComparison.Ordinal)
                && (int)entry.Level >= (int)LogLevel.Information) {
                try {
                    ExampleConsole.RequestLog(entry);
                }
                catch {
                    // Request logging must never interfere with request processing.
                }
            }

            if (_previousSink != null && (int)entry.Level >= (int)_previousMinimumLevel) {
                _previousSink(entry);
            }
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;

            if (_previousSink == null) {
                Log.RemoveSink(_installedSink);
                Log.MinimumLevel = _previousMinimumLevel;
            }
            else {
                Log.SetSink(_previousSink, _previousMinimumLevel);
            }
        }
    }

    #endregion ExampleRequestLogging

    #region ExampleRuntimeFile

    internal static class ExampleRuntimeFile {

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static async Task MaterializeAsync(string path, string content, CancellationToken cancellationToken) {
            string destinationPath = Path.GetFullPath(path);
            string normalizedContent = content.ReplaceLineEndings(Environment.NewLine);

            if (File.Exists(destinationPath)) {
                string existingContent = await File.ReadAllTextAsync(destinationPath, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
                if (string.Equals(existingContent, normalizedContent, StringComparison.Ordinal)) {
                    return;
                }
            }

            string? directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(directory)) {
                throw new InvalidOperationException($"Unable to resolve the directory of runtime file '{destinationPath}'.");
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

            try {
                await File.WriteAllTextAsync(temporaryPath, normalizedContent, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally {
                if (File.Exists(temporaryPath)) {
                    try {
                        File.Delete(temporaryPath);
                    }
                    catch {
                        // Preserve the original materialization error.
                    }
                }
            }
        }
    }

    #endregion ExampleRuntimeFile

    #region ExampleArguments

    internal sealed class ExampleArguments {

        private readonly IReadOnlyDictionary<string, string?> _options;

        public int Port { get; }

        public bool BrowserEnabled { get; private set; }

        public string BrowserPath { get; private set; } = "/";

        public bool IoxideEnabled { get; private set; }

        private ExampleArguments(IReadOnlyDictionary<string, string?> options) {
            _options = options;
            Port = GetInt32("port", 8080, 1, 65535);
        }

        public static ExampleArguments Parse(string[] args) {
            Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < args.Length; index++) {
                string token = args[index];
                if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2) {
                    throw new ExampleCliException($"Unexpected argument '{token}'. Options must use the --name value form.");
                }

                string name = token[2..];
                if (!options.TryAdd(name, null)) {
                    throw new ExampleCliException($"Option '--{name}' was provided more than once.");
                }

                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
                    options[name] = args[++index];
                }
            }

            return new ExampleArguments(options);
        }

        public void EnsureAllowed(params string[] scenarioOptions) {
            HashSet<string> allowed = new(scenarioOptions, StringComparer.OrdinalIgnoreCase) {
            "browser",
            "help",
            "ioxide",
            "log",
            "port"
        };

            string? unknown = _options.Keys.FirstOrDefault(option => !allowed.Contains(option));
            if (unknown != null) {
                throw new ExampleCliException($"Unknown option '--{unknown}' for this scenario.");
            }
        }

        public bool HasFlag(string name) {
            if (!_options.TryGetValue(name, out string? value)) {
                return false;
            }
            if (value != null) {
                throw new ExampleCliException($"Option '--{name}' is a flag and does not accept a value.");
            }
            return true;
        }

        public void ConfigureBrowser(bool enabled, string path) {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Scenario browser path '{path}' must start with '/'.");
            }
            BrowserEnabled = enabled;
            BrowserPath = path;
        }

        public void ConfigureIoxide(bool enabled) {
            if (!enabled) {
                IoxideEnabled = false;
                return;
            }

#if NET10_0
            if (!OperatingSystem.IsLinux()) {
                throw new ExampleCliException("Option '--ioxide' requires Linux with io_uring support.");
            }
            IoxideEnabled = true;
#else
            throw new ExampleCliException("Option '--ioxide' requires the net10.0 target.");
#endif
        }

        public string Require(string name) {
            if (!_options.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value)) {
                throw new ExampleCliException($"Option '--{name}' requires a value.");
            }
            return value;
        }

        public string? Get(string name, string? defaultValue = null) {
            if (!_options.TryGetValue(name, out string? value)) {
                return defaultValue;
            }
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ExampleCliException($"Option '--{name}' requires a value.");
            }
            return value;
        }

        public int GetInt32(string name, int defaultValue, int minimum, int maximum) {
            string? raw = Get(name);
            if (raw == null) {
                return defaultValue;
            }
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < minimum || value > maximum) {
                throw new ExampleCliException($"Option '--{name}' must be an integer between {minimum} and {maximum}.");
            }
            return value;
        }

        public double GetDouble(string name, double defaultValue, double minimum, double maximum) {
            string? raw = Get(name);
            if (raw == null) {
                return defaultValue;
            }
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || !double.IsFinite(value)
                || value < minimum
                || value > maximum) {
                throw new ExampleCliException($"Option '--{name}' must be a number between {minimum} and {maximum}.");
            }
            return value;
        }

        public Uri? GetAbsoluteUri(string name) {
            string? raw = Get(name);
            if (raw == null) {
                return null;
            }
            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)) {
                throw new ExampleCliException($"Option '--{name}' must be an absolute URI.");
            }
            return uri;
        }
    }

    internal sealed class ExampleCliException : Exception {

        public ExampleCliException(string message) : base(message) { }

    }

    #endregion ExampleArguments

    #region ExampleConsole

    internal static class ExampleConsole {

        private static readonly object Sync = new();

        public static void Scenario(string name, string description) {
            Section("SimpleW example");
            WriteLine(false,
                new("  Scenario  ", ConsoleColor.DarkCyan),
                new(name, ConsoleColor.Green)
            );
            WriteLine(false,
                new("  About     ", ConsoleColor.DarkCyan),
                new(description, ConsoleColor.Gray)
            );
            BlankLine();
        }

        public static void Section(string title) {
            WriteLine(false, new Segment(title, ConsoleColor.Cyan));
        }

        public static void Command(string command) {
            WriteLine(false,
                new("  ", ConsoleColor.DarkGray),
                new(command, ConsoleColor.White)
            );
        }

        public static void Item(string name, string description, int nameWidth = 18) {
            WriteLine(false,
                new("  ", ConsoleColor.DarkGray),
                new(name.PadRight(nameWidth), ConsoleColor.Green),
                new(description, ConsoleColor.Gray)
            );
        }

        public static void Muted(string message) {
            WriteLine(false, new Segment(message, ConsoleColor.DarkGray));
        }

        public static void BlankLine(bool writeToError = false) {
            WriteLine(writeToError);
        }

        public static void Info(string message) {
            Status("INFO", ConsoleColor.Cyan, message, writeToError: false);
        }

        public static void Success(string message) {
            Status("OK", ConsoleColor.Green, message, writeToError: false);
        }

        public static void Warning(string message, bool writeToError = false) {
            Status("WARN", ConsoleColor.Yellow, message, writeToError);
        }

        public static void Error(string message) {
            Status("ERROR", ConsoleColor.Red, message, writeToError: true);
        }

        public static void Route(RouteInfo route) {
            ConsoleColor methodColor = GetMethodColor(route.Method);
            string host = string.IsNullOrWhiteSpace(route.Host) ? string.Empty : $"[{route.Host}] ";
            string description = string.IsNullOrWhiteSpace(route.Description) ? string.Empty : $" - {route.Description}";

            WriteLine(false,
                new("  ", ConsoleColor.DarkGray),
                new(route.Method.PadRight(8), methodColor),
                new(" ", ConsoleColor.DarkGray),
                new(host, ConsoleColor.DarkYellow),
                new(route.Path, ConsoleColor.White),
                new(description, ConsoleColor.DarkGray)
            );
        }

        public static void Progress(int current, int total, string path, long size) {
            int width = total.ToString(CultureInfo.InvariantCulture).Length;
            string counter = $"{current.ToString(CultureInfo.InvariantCulture).PadLeft(width)}/{total.ToString(CultureInfo.InvariantCulture)}";
            string formattedSize = size.ToString("N0", CultureInfo.InvariantCulture);

            WriteLine(false,
                new("  [", ConsoleColor.DarkGray),
                new(counter, ConsoleColor.Cyan),
                new("] ", ConsoleColor.DarkGray),
                new(path, ConsoleColor.Gray),
                new($" ({formattedSize} bytes)", ConsoleColor.DarkGray)
            );
        }

        public static void Trace(string? method, string? path, string? statusCode, double durationMilliseconds) {
            string safeMethod = string.IsNullOrWhiteSpace(method) ? "-" : method;
            string safePath = string.IsNullOrWhiteSpace(path) ? "-" : path;
            string safeStatus = string.IsNullOrWhiteSpace(statusCode) ? "-" : statusCode;
            ConsoleColor statusColor = safeStatus[0] switch {
                '2' or '3' => ConsoleColor.Green,
                '4' => ConsoleColor.Yellow,
                '5' => ConsoleColor.Red,
                _ => ConsoleColor.Gray
            };

            WriteLine(false,
                new("[TRACE] ", ConsoleColor.Magenta),
                new(safeMethod.PadRight(8), GetMethodColor(safeMethod)),
                new(" ", ConsoleColor.DarkGray),
                new(safePath, ConsoleColor.White),
                new("  ", ConsoleColor.DarkGray),
                new(safeStatus, statusColor),
                new($"  {durationMilliseconds.ToString("F1", CultureInfo.InvariantCulture)} ms", ConsoleColor.DarkGray)
            );
        }

        public static void RequestLog(LogEntry entry) {
            bool writeToError = entry.Level is LogLevel.Error or LogLevel.Fatal;
            string timestamp = entry.LocalTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string exception = entry.Exception == null ? string.Empty : $" - {entry.Exception.Message}";

            if (!TryParseRequestLog(entry.Message, out HttpRequestLog request)) {
                ConsoleColor levelColor = GetLogLevelColor(entry.Level);
                WriteLine(writeToError,
                    new(timestamp, ConsoleColor.DarkGray),
                    new($" [{entry.LevelName3Len}] ", levelColor),
                    new(entry.Source, ConsoleColor.DarkCyan),
                    new(" ", ConsoleColor.DarkGray),
                    new(entry.Message, ConsoleColor.Gray),
                    new(exception, ConsoleColor.Red)
                );
                return;
            }

            WriteLine(writeToError,
                new(timestamp, ConsoleColor.DarkGray),
                new(" [HTTP] ", ConsoleColor.Cyan),
                new(request.Method.PadRight(8), GetMethodColor(request.Method)),
                new(" ", ConsoleColor.DarkGray),
                new(request.Path, ConsoleColor.White),
                new("  ", ConsoleColor.DarkGray),
                new(request.StatusCode, GetStatusColor(request.StatusCode)),
                new($"  {request.DurationMilliseconds} ms", ConsoleColor.DarkGray),
                new($"  {request.Session}", ConsoleColor.DarkGray),
                new($"  {request.ClientIp}", ConsoleColor.DarkGray),
                new(exception, ConsoleColor.Red)
            );
        }

        private static bool TryParseRequestLog(string message, out HttpRequestLog request) {
            request = default;

            int methodEnd = message.IndexOf(' ');
            if (methodEnd <= 0 || methodEnd + 2 >= message.Length || message[methodEnd + 1] != '"') {
                return false;
            }

            int pathStart = methodEnd + 2;
            int pathEnd = message.IndexOf('"', pathStart);
            if (pathEnd < pathStart) {
                return false;
            }

            string[] fields = message[(pathEnd + 1)..]
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length < 3
                || fields[0].Length != 3
                || !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int statusCode)
                || statusCode is < 100 or > 999
                || !fields[1].EndsWith("ms", StringComparison.Ordinal)
                || !long.TryParse(fields[1][..^2], NumberStyles.None, CultureInfo.InvariantCulture, out long durationMilliseconds)) {
                return false;
            }

            request = new HttpRequestLog(
                message[..methodEnd],
                message[pathStart..pathEnd],
                fields[0],
                durationMilliseconds,
                fields[2],
                fields.Length >= 4 ? fields[3] : "-"
            );
            return true;
        }

        private static void Status(string label, ConsoleColor color, string message, bool writeToError) {
            WriteLine(writeToError,
                new($"[{label}] ", color),
                new(message, color)
            );
        }

        private static ConsoleColor GetMethodColor(string method) => method.ToUpperInvariant() switch {
            "GET" => ConsoleColor.Green,
            "POST" => ConsoleColor.Cyan,
            "PUT" => ConsoleColor.Yellow,
            "PATCH" => ConsoleColor.Magenta,
            "DELETE" => ConsoleColor.Red,
            "HEAD" => ConsoleColor.DarkCyan,
            "OPTIONS" => ConsoleColor.DarkCyan,
            _ => ConsoleColor.Gray
        };

        private static ConsoleColor GetStatusColor(string statusCode) => statusCode[0] switch {
            '2' or '3' => ConsoleColor.Green,
            '4' => ConsoleColor.Yellow,
            '5' => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };

        private static ConsoleColor GetLogLevelColor(LogLevel level) => level switch {
            LogLevel.Trace => ConsoleColor.DarkGray,
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Information => ConsoleColor.Cyan,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Fatal => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };

        private static void WriteLine(bool writeToError, params Segment[] segments) {
            TextWriter writer = writeToError ? Console.Error : Console.Out;

            lock (Sync) {
                if (!IsColorEnabled(writeToError) || !TryGetForegroundColor(out ConsoleColor originalColor)) {
                    WritePlainLine(writer, segments);
                    return;
                }

                try {
                    foreach (Segment segment in segments) {
                        if (segment.Color.HasValue) {
                            TrySetForegroundColor(segment.Color.Value);
                        }
                        writer.Write(segment.Text);
                    }
                    writer.WriteLine();
                }
                finally {
                    TrySetForegroundColor(originalColor);
                }
            }
        }

        private static void WritePlainLine(TextWriter writer, IEnumerable<Segment> segments) {
            foreach (Segment segment in segments) {
                writer.Write(segment.Text);
            }
            writer.WriteLine();
        }

        private static bool IsColorEnabled(bool writeToError) {
            if (Environment.GetEnvironmentVariable("NO_COLOR") != null
                || string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            return writeToError ? !Console.IsErrorRedirected : !Console.IsOutputRedirected;
        }

        private static bool TryGetForegroundColor(out ConsoleColor color) {
            try {
                color = Console.ForegroundColor;
                return true;
            }
            catch {
                color = ConsoleColor.Gray;
                return false;
            }
        }

        private static void TrySetForegroundColor(ConsoleColor color) {
            try {
                Console.ForegroundColor = color;
            }
            catch {
                // Color is cosmetic; never let an unsupported console break the example.
            }
        }

        private readonly record struct Segment(string Text, ConsoleColor? Color = null);

        private readonly record struct HttpRequestLog(
            string Method,
            string Path,
            string StatusCode,
            long DurationMilliseconds,
            string Session,
            string ClientIp
        );
    }

    #endregion ExampleConsole

}
