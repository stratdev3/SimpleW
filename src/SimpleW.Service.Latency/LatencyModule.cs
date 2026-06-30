using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Latency {

    /// <summary>
    /// LatencyModule
    /// </summary>
    internal sealed class LatencyModule : IHttpModule {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger _log = new Logger<LatencyModule>();

        /// <summary>
        /// Options
        /// </summary>
        private readonly LatencyOptions _options;

        /// <summary>
        /// O(1) exact rules
        /// </summary>
        private readonly Dictionary<string, TimeSpan> _exactMap;

        /// <summary>
        /// Prefix rules (wildcard or trailing "/")
        /// </summary>
        private readonly PrefixRule[] _prefixRules;

        /// <summary>
        /// Optional: "*" rule (catch-all with priority over GlobalLatency)
        /// </summary>
        private readonly TimeSpan? _starRule;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public LatencyModule(LatencyOptions options) {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            (_exactMap, _prefixRules, _starRule) = BuildRuleTables(_options.Rules);
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        public void Install(SimpleWServer server) {
            if (server.IsStarted) {
                InvalidOperationException ex = new("LatencyModule must be installed before server start.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }

            _log.Info("installing...");

            server.UseMiddleware(async (session, next) => {

                if (_options.Enabled) {
                    TimeSpan? latency = ResolveLatency(session.Request.Path);

                    if (latency is { } d && d > TimeSpan.Zero) {
                        await Task.Delay(d).ConfigureAwait(false);
                    }
                }

                await next().ConfigureAwait(false);
            });

            _log.Info("installed");
        }

        private TimeSpan? ResolveLatency(string path) {

            if (!string.IsNullOrEmpty(path)) {

                // 1) exact
                if (_exactMap.TryGetValue(path, out var exact)) {
                    return exact;
                }

                // 2) prefix rules (most specific first)
                for (int i = 0; i < _prefixRules.Length; i++) {
                    ref readonly var pr = ref _prefixRules[i];
                    if (path.StartsWith(pr.Prefix, StringComparison.Ordinal)) {
                        return pr.Latency;
                    }
                }
            }

            // 3) optional "*" rule
            if (_starRule.HasValue) {
                return _starRule.Value;
            }

            // 4) fallback global
            return _options.GlobalLatency;
        }

        /// <summary>
        /// Build Rules (Filter/Order)
        /// </summary>
        /// <param name="rules"></param>
        /// <returns></returns>
        private static (Dictionary<string, TimeSpan> exactMap, PrefixRule[] prefixRules, TimeSpan? starRule)
            BuildRuleTables(List<LatencyRule> rules) {

            Dictionary<string, TimeSpan> exact = new(StringComparer.Ordinal);
            List<PrefixRule> prefix = new();
            TimeSpan? star = null;

            for (int i = 0; i < rules.Count; i++) {
                LatencyRule r = rules[i];

                // Drop useless (0 latency)
                if (r.Latency == TimeSpan.Zero) {
                    continue;
                }

                string p = NormalizePath(r.Path);

                if (p == "*") {
                    // last one wins for star; you could also "first wins" if you prefer
                    star = r.Latency;
                    continue;
                }

                if (IsPrefixRule(p)) {
                    prefix.Add(new PrefixRule(GetPrefix(p), r.Latency));
                }
                else {
                    // exact path
                    // last one wins (simple and predictable)
                    exact[p] = r.Latency;
                }
            }

            // Most specific first => longer prefix first
            prefix.Sort(static (a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));

            return (exact, prefix.ToArray(), star);
        }

        #region helpers

        /// <summary>
        /// NormalizePath
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string NormalizePath(string path) {
            path = path.Trim();

            if (path == "*") {
                return "*";
            }

            // Normalize missing leading slash, unless it's obviously invalid
            if (!path.StartsWith('/')) {
                path = "/" + path;
            }

            // Normalize repeated slashes
            while (path.Contains("//", StringComparison.Ordinal)) {
                path = path.Replace("//", "/", StringComparison.Ordinal);
            }

            return path;
        }

        /// <summary>
        /// Prefix rules:
        /// - ends with "/*" (explicit wildcard)
        /// - ends with "/"  (implicit wildcard)
        /// </summary>
        private static bool IsPrefixRule(string path) => path.EndsWith("/*", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal);

        /// <summary>
        /// GetPrefix
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string GetPrefix(string path) {
            if (path.EndsWith("/*", StringComparison.Ordinal)) {
                return path[..^1]; // keep the trailing "/" => "/api/"
            }
            // ends with "/"
            return path;
        }

        /// <summary>
        /// PrefixRule
        /// </summary>
        /// <param name="Prefix"></param>
        /// <param name="Latency"></param>
        private readonly record struct PrefixRule(string Prefix, TimeSpan Latency);

        #endregion helpers

    }

}
