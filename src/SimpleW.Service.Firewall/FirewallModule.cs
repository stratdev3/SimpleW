using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.CompilerServices;
using SimpleW.Modules;
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

    /// <summary>
    /// Per-server firewall module state.
    /// </summary>
    internal sealed class ModuleState {

        public object SyncRoot { get; } = new();

        public bool IsInstalled { get; set; }

        private volatile ModuleConfiguration? _configuration;

        public ModuleConfiguration? Snapshot => _configuration;

        public void SetConfiguration(ModuleConfiguration configuration) {
            lock (SyncRoot) {
                _configuration = configuration;
            }
        }

    }

    /// <summary>
    /// Immutable runtime configuration used by the middleware.
    /// </summary>
    internal sealed record ModuleConfiguration(
        FirewallPolicy GlobalPolicy,
        TimeSpan StateTtl,
        int MaxTrackedIps,
        int CleanupEveryNRequests,
        bool EnableTelemetry,
        string? MaxMindCountryDbPath,
        bool TreatUnknownCountryAsMatchable,
        TimeSpan? CountryCacheTtl
    ) {

        public static ModuleConfiguration FromOptions(FirewallModuleExtension.FirewallOptions options) {
            ArgumentNullException.ThrowIfNull(options);

            return new ModuleConfiguration(
                GlobalPolicy: FirewallPolicy.FromGlobal(options),
                StateTtl: options.StateTtl,
                MaxTrackedIps: options.MaxTrackedIps,
                CleanupEveryNRequests: options.CleanupEveryNRequests,
                EnableTelemetry: options.EnableTelemetry,
                MaxMindCountryDbPath: options.MaxMindCountryDbPath,
                TreatUnknownCountryAsMatchable: options.TreatUnknownCountryAsMatchable,
                CountryCacheTtl: options.CountryCacheTtl
            );
        }

        public TimeSpan EffectiveCountryCacheTtl => CountryCacheTtl ?? StateTtl;

    }

    /// <summary>
    /// Firewall middleware module.
    /// </summary>
    internal sealed class FirewallModule : IHttpModule {

        private readonly ILogger _log = new Logger<FirewallModule>();

        private readonly ModuleState _state;

        private readonly FirewallRateLimiter _rateLimiter = new();

        private readonly GeoIpCountryResolver _countryResolver = new();

        private long _requestCounter;

        private FirewallTelemetry? _telemetry;

        private readonly object _telemetryLock = new();

        public FirewallModule(ModuleState state) {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public int FixedTrackedIpCount => _rateLimiter.FixedCount;

        public int SlidingTrackedIpCount => _rateLimiter.SlidingCount;

        public int CountryCacheCount => _countryResolver.CacheCount;

        public int GlobalAllowRuleCount => _state.Snapshot?.GlobalPolicy.AllowIpRules.Length ?? 0;

        public int GlobalDenyRuleCount => _state.Snapshot?.GlobalPolicy.DenyIpRules.Length ?? 0;

        public int GlobalAllowCountryRuleCount => _state.Snapshot?.GlobalPolicy.AllowCountryRules.Length ?? 0;

        public int GlobalDenyCountryRuleCount => _state.Snapshot?.GlobalPolicy.DenyCountryRules.Length ?? 0;

        /// <summary>
        /// Installs the middleware into the server pipeline.
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Install(SimpleWServer server) {
            if (server.IsStarted) {
                InvalidOperationException ex = new("FirewallModule must be installed before server start.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }

            _log.Info("installing...");
            server.UseMiddleware(MiddlewareAsync);
            _log.Info("installed");
        }

        private async ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next) {
            ModuleConfiguration? configuration = _state.Snapshot;
            if (configuration == null) {
                await next().ConfigureAwait(false);
                return;
            }

            long ts0 = Stopwatch.GetTimestamp();
            FirewallTelemetry? telemetry = EnsureTelemetry(configuration.EnableTelemetry, session.Server);

            IPAddress? ip = session.ClientIpAddress;
            if (ip == null) {
                RecordDecision(telemetry, ts0, "blocked", "no_ip", "global");
                telemetry?.BlockedTotal.Add(1, CreateTags("blocked", "no_ip", "global"));
                await session.Response.Status(403).SendAsync().ConfigureAwait(false);
                return;
            }

            MaybeCleanup(configuration);

            FirewallPolicy policy = ResolvePolicy(session.Metadata, configuration);
            string scope = policy.ScopeTag;

            string? countryIso2 = policy.HasCountryRules
                                      ? _countryResolver.ResolveCountryIso2(ip, configuration)
                                      : null;

            if (policy.MatchesDenyIp(ip)) {
                RecordDecision(telemetry, ts0, "blocked", $"{scope}_deny", scope);
                telemetry?.BlockedTotal.Add(1, CreateTags("blocked", $"{scope}_deny", scope));
                telemetry?.DenyMatchTotal.Add(1, CreateMatchTags(scope));
                await session.Response.Status(403).SendAsync().ConfigureAwait(false);
                return;
            }

            if (policy.MatchesDenyCountry(countryIso2, configuration.TreatUnknownCountryAsMatchable)) {
                RecordDecision(telemetry, ts0, "blocked", $"{scope}_country_deny", scope);
                telemetry?.BlockedTotal.Add(1, CreateTags("blocked", $"{scope}_country_deny", scope));
                telemetry?.DenyMatchTotal.Add(1, CreateMatchTags(scope));
                await session.Response.Status(403).SendAsync().ConfigureAwait(false);
                return;
            }

            if (policy.HasAllowRules) {
                bool allowMatched = policy.MatchesAllowIp(ip)
                                    || policy.MatchesAllowCountry(countryIso2, configuration.TreatUnknownCountryAsMatchable);

                if (!allowMatched) {
                    RecordDecision(telemetry, ts0, "blocked", $"{scope}_allow_miss", scope);
                    telemetry?.BlockedTotal.Add(1, CreateTags("blocked", $"{scope}_allow_miss", scope));
                    telemetry?.AllowMissTotal.Add(1, CreateMatchTags(scope));
                    await session.Response.Status(403).SendAsync().ConfigureAwait(false);
                    return;
                }

                telemetry?.AllowMatchTotal.Add(1, CreateMatchTags(scope));
            }

            RateLimitOptions? rateLimit = policy.RateLimit ?? (policy.IsHandlerPolicy ? configuration.GlobalPolicy.RateLimit : null);
            if (rateLimit != null && _rateLimiter.IsRateLimited(ip, rateLimit)) {
                TagList tags = CreateTags("rate_limited", "rate_limited", scope);
                tags.Add("window", rateLimit.SlidingWindow ? "sliding" : "fixed");

                telemetry?.DecisionsTotal.Add(1, tags);
                telemetry?.RateLimitedTotal.Add(1, tags);
                telemetry?.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(ts0).TotalMilliseconds, tags);

                await session.Response.Status(429).SendAsync().ConfigureAwait(false);
                return;
            }

            RecordDecision(telemetry, ts0, "allowed", "ok", scope);
            await next().ConfigureAwait(false);
        }

        private void MaybeCleanup(ModuleConfiguration configuration) {
            long n = Interlocked.Increment(ref _requestCounter);

            bool rateOverCap = _rateLimiter.IsOverCap(configuration.MaxTrackedIps);
            bool countryOverCap = _countryResolver.IsOverCap(configuration.MaxTrackedIps);

            if (!rateOverCap && !countryOverCap && (n % configuration.CleanupEveryNRequests != 0)) {
                return;
            }

            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            _rateLimiter.Cleanup(nowTicks, configuration);
            _countryResolver.Cleanup(nowTicks, configuration);
        }

        private static FirewallPolicy ResolvePolicy(HandlerMetadataCollection metadata, ModuleConfiguration configuration) {
            return FirewallPolicy.TryFromMetadata(metadata, out FirewallPolicy? handlerPolicy)
                       ? handlerPolicy!
                       : configuration.GlobalPolicy;
        }

        private FirewallTelemetry? EnsureTelemetry(bool enable, SimpleWServer server) {
            if (!enable) {
                return null;
            }

            Telemetry? telemetry = server.Telemetry;
            if (telemetry == null || !server.IsTelemetryEnabled) {
                return null;
            }

            FirewallTelemetry? current = _telemetry;
            if (current != null) {
                return current;
            }

            lock (_telemetryLock) {
                _telemetry ??= new FirewallTelemetry(telemetry.Meter, this);
                return _telemetry;
            }
        }

        private static void RecordDecision(FirewallTelemetry? telemetry, long ts0, string result, string reason, string scope) {
            if (telemetry == null) {
                return;
            }

            TagList tags = CreateTags(result, reason, scope);
            telemetry.DecisionsTotal.Add(1, tags);
            telemetry.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(ts0).TotalMilliseconds, tags);
        }

        private static TagList CreateTags(string result, string reason, string scope) {
            TagList tags = default;
            tags.Add("result", result);
            tags.Add("reason", reason);
            tags.Add("scope", scope);
            return tags;
        }

        private static TagList CreateMatchTags(string scope) {
            TagList tags = default;
            tags.Add("scope", scope);
            return tags;
        }

    }

}
