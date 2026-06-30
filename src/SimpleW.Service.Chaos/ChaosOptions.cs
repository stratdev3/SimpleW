using SimpleW.Observability;


namespace SimpleW.Service.Chaos {

    /// <summary>
    /// ChaosOptions
    /// </summary>
    public sealed class ChaosOptions {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger _log = new Logger<ChaosOptions>();

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
                ArgumentException ex = new ($"{nameof(FixedStatusCode)} must be a valid HTTP status code.");
                _log.Fatal(ex.Message, ex);
                throw ex;
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

}
