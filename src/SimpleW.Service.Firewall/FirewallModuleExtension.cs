using System.Runtime.CompilerServices;
using SimpleW.Observability;


namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Firewall module extensions for SimpleW.
    /// </summary>
    public static class FirewallModuleExtension {

        private static readonly ConditionalWeakTable<SimpleWServer, ModuleState> _states = new();

        private static readonly ILogger _log = new Logger<FirewallModule>();

        /// <summary>
        /// Adds or updates the firewall module on the current server.
        /// Global rules are configured here; handler-specific rules are declared with firewall metadata attributes.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseFirewallModule(this SimpleWServer server, Action<FirewallOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            FirewallOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            ModuleState state = _states.GetValue(server, static _ => new ModuleState());
            state.SetConfiguration(ModuleConfiguration.FromOptions(options));

            EnsureInstalled(server, state);
            _log.Info("installed");
            return server;
        }

        private static void EnsureInstalled(SimpleWServer server, ModuleState state) {
            lock (state.SyncRoot) {
                if (state.IsInstalled) {
                    return;
                }

                state.IsInstalled = true;
            }

            server.UseModule(new FirewallModule(state));
        }

        /// <summary>
        /// Module-level firewall configuration.
        /// Path-based rules are declared on handlers with firewall metadata attributes.
        /// </summary>
        public sealed class FirewallOptions {

            private readonly ILogger _log = new Logger<FirewallOptions>();

            /// <summary>
            /// Global allow list by IP/CIDR.
            /// If not empty, every request not matching an allow rule is denied by default.
            /// </summary>
            public List<IpRule> AllowRules { get; } = new();

            /// <summary>
            /// Global deny list by IP/CIDR.
            /// </summary>
            public List<IpRule> DenyRules { get; } = new();

            /// <summary>
            /// Global rate limit. Null disables global rate limiting.
            /// </summary>
            public RateLimitOptions? GlobalRateLimit { get; set; }

            /// <summary>
            /// IP/CIDR rules that bypass global and handler rate limiting.
            /// These rules do not bypass allow or deny firewall rules.
            /// </summary>
            public List<IpRule> RateLimitWhitelistRules { get; } = new();

            /// <summary>
            /// Retention for inactive per-IP state.
            /// </summary>
            public TimeSpan StateTtl { get; set; } = TimeSpan.FromMinutes(10);

            /// <summary>
            /// Safety cap for tracked IP state.
            /// </summary>
            public int MaxTrackedIps { get; set; } = 50_000;

            /// <summary>
            /// Opportunistic cleanup frequency.
            /// </summary>
            public int CleanupEveryNRequests { get; set; } = 10_000;

            /// <summary>
            /// Enables firewall telemetry when the underlying SimpleWServer telemetry is enabled too.
            /// </summary>
            public bool EnableTelemetry { get; set; }

            /// <summary>
            /// Optional MaxMind GeoIP country database path (.mmdb).
            /// Country rules are treated as unknown when this value is null or empty.
            /// </summary>
            public string? MaxMindCountryDbPath { get; set; }

            /// <summary>
            /// If true, unresolved countries can match CountryRule.Unknown().
            /// </summary>
            public bool TreatUnknownCountryAsMatchable { get; set; } = true;

            /// <summary>
            /// Cache duration for IP to country resolution. If null, StateTtl is used.
            /// </summary>
            public TimeSpan? CountryCacheTtl { get; set; }

            /// <summary>
            /// Global allow list by country.
            /// If not empty, every request not matching an allow country is denied by default.
            /// </summary>
            public List<CountryRule> AllowCountries { get; } = new();

            /// <summary>
            /// Global deny list by country.
            /// </summary>
            public List<CountryRule> DenyCountries { get; } = new();

            /// <summary>
            /// Validates and normalizes options.
            /// </summary>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            public FirewallOptions ValidateAndNormalize() {
                if (StateTtl <= TimeSpan.Zero) {
                    ArgumentException ex = new($"{nameof(StateTtl)} must be > 0.", nameof(StateTtl));
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
                if (MaxTrackedIps <= 0) {
                    ArgumentException ex = new($"{nameof(MaxTrackedIps)} must be > 0.", nameof(MaxTrackedIps));
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
                if (CleanupEveryNRequests <= 0) {
                    ArgumentException ex = new($"{nameof(CleanupEveryNRequests)} must be > 0.", nameof(CleanupEveryNRequests));
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }
                if (CountryCacheTtl != null && CountryCacheTtl <= TimeSpan.Zero) {
                    ArgumentException ex = new($"{nameof(CountryCacheTtl)} must be > 0.", nameof(CountryCacheTtl));
                    _log.Fatal(ex.Message, ex);
                    throw ex;
                }

                MaxMindCountryDbPath = string.IsNullOrWhiteSpace(MaxMindCountryDbPath) ? null : MaxMindCountryDbPath.Trim();
                return this;
            }

        }

    }

}
