using SimpleW.Observability;


namespace SimpleW.Service.Latency {

    /// <summary>
    /// LatencyOptions
    /// </summary>
    public class LatencyOptions {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger _log = new Logger<LatencyOptions>();

        /// <summary>
        /// Enable/Disable module
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Global latency if no rule matches
        /// </summary>
        public TimeSpan? GlobalLatency { get; set; }

        /// <summary>
        /// Rules:
        /// - Exact: "/api/foo"
        /// - Prefix: "/api/*" or "/api/" ("/api/" is treated as "/api/*")
        /// - "*" means match everything (like global but with rule priority)
        /// First match wins (after exact match).
        /// </summary>
        public List<LatencyRule> Rules { get; } = new();

        /// <summary>
        /// Check Properties and return
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal void Validate() {
            if (GlobalLatency < TimeSpan.Zero) {
                ArgumentOutOfRangeException ex = new(nameof(GlobalLatency));
                _log.Fatal(ex.Message, ex);
                throw ex;
            }

            foreach (var r in Rules) {
                if (string.IsNullOrWhiteSpace(r.Path)) {
                    ArgumentException ex = new($"{nameof(Path)} cannot be empty.", nameof(Path));
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
                if (r.Latency < TimeSpan.Zero) {
                    ArgumentOutOfRangeException ex = new(nameof(Rules), "Latency must be >= 0.");
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
            }
        }
    }

}
