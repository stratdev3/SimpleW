using System.Net.Sockets;
using SimpleW.Modules;


namespace SimpleW.Service.Chaos {

    /// <summary>
    /// ChaosModuleExtension
    /// </summary>
    public static class ChaosModuleExtension {

        /// <summary>
        /// Use Chaos Module (inject HTTP errors / connection failures)
        /// </summary>
        /// <example>
        /// server.UseChaosModule(o => {
        ///     o.Enabled = true;
        ///     o.Prefix = "/api";
        ///     o.Probability = 0.10; // 10% of requests are sabotaged
        ///     o.CloseConnectionProbability = 0.20; // 20% of sabotaged requests: connection drop
        ///     o.AbortWithRst = true; // RST instead of graceful close
        ///     o.StatusWeights = new Dictionary int, double {
        ///         [403] = 1,
        ///         [404] = 2,
        ///         [500] = 4
        ///     };
        ///     o.MinDelayMs = 0;
        ///     o.MaxDelayMs = 250;
        /// });
        /// </example>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseChaosModule(this SimpleWServer server, Action<ChaosOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            ChaosOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new ChaosModule(options));
            return server;
        }

        /// <summary>
        /// ChaosOptions
        /// </summary>
        public sealed class ChaosOptions {

            /// <summary>Enable/disable chaos</summary>
            public bool Enabled { get; set; } = false;

            /// <summary>
            /// Apply chaos only when request path starts with this prefix (default: "/api")
            /// </summary>
            public string Prefix { get; set; } = "/api";

            /// <summary>
            /// Apply chaos only for these HTTP methods (null/empty => all methods)
            /// Example: ["GET","POST"]
            /// </summary>
            public HashSet<string>? Methods { get; set; }

            /// <summary>
            /// Probability [0..1] of injecting chaos for matching requests
            /// </summary>
            public double Probability { get; set; } = 0.0;

            /// <summary>
            /// Among sabotaged requests, probability [0..1] to drop the connection (no response).
            /// </summary>
            public double CloseConnectionProbability { get; set; } = 0.0;

            /// <summary>
            /// If true, abort using TCP RST (linger enable=true, seconds=0) before closing.
            /// If false, close gracefully.
            /// </summary>
            public bool AbortWithRst { get; set; } = true;

            /// <summary>
            /// Optional artificial latency on sabotaged requests.
            /// </summary>
            public int MinDelayMs { get; set; } = 0;

            /// <summary>
            /// Optional artificial latency on sabotaged requests.
            /// </summary>
            public int MaxDelayMs { get; set; } = 0;

            /// <summary>
            /// If set, forces a single status code instead of weighted random.
            /// Example: 500
            /// </summary>
            public int? FixedStatusCode { get; set; }

            /// <summary>
            /// Weighted HTTP status codes when not using FixedStatusCode.
            /// Keys are status codes (e.g., 403/404/500) values are weights (>0).
            /// If empty/null => defaults to {500:1}
            /// </summary>
            public Dictionary<int, double>? StatusWeights { get; set; } = new Dictionary<int, double> {
                [500] = 1
            };

            /// <summary>
            /// Optional seed for reproducible chaos.
            /// </summary>
            public int? Seed { get; set; }

            /// <summary>
            /// Response body template (text/plain). "{code}" and "{text}" are replaced.
            /// Set null/empty to send no body.
            /// </summary>
            public string? BodyTemplate { get; set; } = "CHAOS: simulated error {code} {text}";

            /// <summary>
            /// Check Properties and return
            /// </summary>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            internal ChaosOptions ValidateAndNormalize() {
                Prefix = SimpleWExtension.NormalizePrefix(string.IsNullOrWhiteSpace(Prefix) ? "/api" : Prefix);

                Probability = Clamp01(Probability);
                CloseConnectionProbability = Clamp01(CloseConnectionProbability);

                if (MinDelayMs < 0) {
                    MinDelayMs = 0;
                }
                if (MaxDelayMs < 0) {
                    MaxDelayMs = 0;
                }
                if (MaxDelayMs < MinDelayMs) {
                    MaxDelayMs = MinDelayMs;
                }

                if (Methods is { Count: > 0 }) {
                    // normalize methods to upper-case
                    Methods = new HashSet<string>(Methods.Select(m => (m ?? string.Empty).Trim().ToUpperInvariant())
                                                         .Where(m => m.Length > 0),
                                                  StringComparer.Ordinal);
                    if (Methods.Count == 0) {
                        Methods = null;
                    }
                }
                else {
                    Methods = null;
                }

                if (FixedStatusCode.HasValue && (FixedStatusCode.Value < 100 || FixedStatusCode.Value > 999)) {
                    throw new ArgumentException($"{nameof(ChaosOptions)}.{nameof(FixedStatusCode)} must be a valid HTTP status code.");
                }

                if (StatusWeights == null || StatusWeights.Count == 0) {
                    StatusWeights = new Dictionary<int, double> { [500] = 1 };
                }
                else {
                    // purge invalid entries
                    var cleaned = new Dictionary<int, double>();
                    foreach (var kv in StatusWeights) {
                        int code = kv.Key;
                        double w = kv.Value;
                        if (code < 100 || code > 999) {
                            continue;
                        }
                        if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0) {
                            continue;
                        }
                        cleaned[code] = w;
                    }
                    if (cleaned.Count == 0) {
                        cleaned[500] = 1;
                    }
                    StatusWeights = cleaned;
                }

                return this;
            }

            private static double Clamp01(double v) {
                if (double.IsNaN(v)) {
                    return 0;
                }
                if (v < 0) {
                    return 0;
                }
                if (v > 1) {
                    return 1;
                }
                return v;
            }

        }

        /// <summary>
        /// Chaos Module
        /// </summary>
        private sealed class ChaosModule : IHttpModule {

            /// <summary>
            /// Options
            /// </summary>
            private readonly ChaosOptions _options;

            /// <summary>
            /// Random Generator
            /// </summary>
            private readonly Random _rng;

            public ChaosModule(ChaosOptions options) {
                _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
                _rng = _options.Seed.HasValue ? new Random(_options.Seed.Value) : new Random();
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("ChaosModule must be installed before server start.");
                }

                // Middleware = before routing -> affects everything under Prefix (API)
                server.UseMiddleware(InvokeAsync);
            }

            /// <summary>
            /// Handler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="next"></param>
            /// <returns></returns>
            private async ValueTask InvokeAsync(HttpSession session, Func<ValueTask> next) {
                if (!_options.Enabled) {
                    await next().ConfigureAwait(false);
                    return;
                }

                HttpRequest req = session.Request;

                // Path filter
                if (string.IsNullOrEmpty(req.Path) || !req.Path.StartsWith(_options.Prefix, StringComparison.Ordinal)) {
                    await next().ConfigureAwait(false);
                    return;
                }

                // Method filter
                if (_options.Methods is not null && !_options.Methods.Contains(req.Method)) {
                    await next().ConfigureAwait(false);
                    return;
                }

                // Probability gate
                if (_options.Probability <= 0 || _rng.NextDouble() >= _options.Probability) {
                    await next().ConfigureAwait(false);
                    return;
                }

                // Optional delay
                if (_options.MaxDelayMs > 0) {
                    int delay = _options.MinDelayMs == _options.MaxDelayMs
                                    ? _options.MinDelayMs
                                    : _rng.Next(_options.MinDelayMs, _options.MaxDelayMs + 1);

                    if (delay > 0) {
                        await Task.Delay(delay).ConfigureAwait(false);
                    }
                }

                // Connection drop?
                if (_options.CloseConnectionProbability > 0 && _rng.NextDouble() < _options.CloseConnectionProbability) {
                    try {

                        if (_options.AbortWithRst) {
                            // Force TCP RST on close
                            session.Socket.LingerState = new LingerOption(enable: true, seconds: 0);
                        }

                        // No response, just bye-bye.
                        session.Dispose();
                    }
                    catch {
                        // swallow: this is chaos, not a church
                    }
                    return;
                }

                // HTTP error response
                int code = _options.FixedStatusCode ?? PickWeightedStatus(_options.StatusWeights!);
                string text = HttpResponse.DefaultStatusText(code);

                // by forcing Connection header to close, then the session.CloseAfterResponse = true
                session.Response.AddHeader("Connection", "close");

                string? body = _options.BodyTemplate;
                if (!string.IsNullOrWhiteSpace(body)) {
                    body = body.Replace("{code}", code.ToString())
                               .Replace("{text}", text);
                    await session.Response.Status(code, text).Text(body).SendAsync().ConfigureAwait(false);
                }
                else {
                    await session.Response.Status(code, text).RemoveBody().SendAsync().ConfigureAwait(false);
                }
            }

            private int PickWeightedStatus(Dictionary<int, double> weights) {
                // weights already validated (count>0, w>0)
                double total = 0;
                foreach (var w in weights.Values) {
                    total += w;
                }

                // just in case of weird floating point: fallback
                if (total <= 0) {
                    return 500;
                }

                double r = _rng.NextDouble() * total;
                foreach (var kv in weights) {
                    r -= kv.Value;
                    if (r <= 0) {
                        return kv.Key;
                    }
                }

                // fallback (shouldn't happen)
                return weights.Keys.FirstOrDefault(500);
            }

        }

    }

}
