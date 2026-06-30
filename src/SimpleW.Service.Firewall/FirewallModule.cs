using System.Diagnostics;
using System.Net;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Firewall {

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
        TimeSpan? CountryCacheTtl,
        IpRule[] RateLimitWhitelistRules
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
                CountryCacheTtl: options.CountryCacheTtl,
                RateLimitWhitelistRules: options.RateLimitWhitelistRules.ToArray()
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
                AddFirewallBlockedEvent(telemetry, session, null, "no_ip", "global");
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
                AddFirewallBlockedEvent(telemetry, session, ip, $"{scope}_deny", scope);
                RecordDecision(telemetry, ts0, "blocked", $"{scope}_deny", scope);
                telemetry?.BlockedTotal.Add(1, CreateTags("blocked", $"{scope}_deny", scope));
                telemetry?.DenyMatchTotal.Add(1, CreateMatchTags(scope));
                await session.Response.Status(403).SendAsync().ConfigureAwait(false);
                return;
            }

            if (policy.MatchesDenyCountry(countryIso2, configuration.TreatUnknownCountryAsMatchable)) {
                AddFirewallBlockedEvent(telemetry, session, ip, $"{scope}_country_deny", scope);
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
                    AddFirewallBlockedEvent(telemetry, session, ip, $"{scope}_allow_miss", scope);
                    RecordDecision(telemetry, ts0, "blocked", $"{scope}_allow_miss", scope);
                    telemetry?.BlockedTotal.Add(1, CreateTags("blocked", $"{scope}_allow_miss", scope));
                    telemetry?.AllowMissTotal.Add(1, CreateMatchTags(scope));
                    await session.Response.Status(403).SendAsync().ConfigureAwait(false);
                    return;
                }

                telemetry?.AllowMatchTotal.Add(1, CreateMatchTags(scope));
            }

            RateLimitOptions? rateLimit = policy.RateLimit ?? (policy.IsHandlerPolicy ? configuration.GlobalPolicy.RateLimit : null);
            if (rateLimit != null
                && !MatchesAny(configuration.RateLimitWhitelistRules, ip)
                && _rateLimiter.IsRateLimited(ip, rateLimit)) {
                TagList tags = CreateTags("rate_limited", "rate_limited", scope);
                tags.Add("window", rateLimit.SlidingWindow ? "sliding" : "fixed");

                telemetry?.DecisionsTotal.Add(1, tags);
                telemetry?.RateLimitedTotal.Add(1, tags);
                telemetry?.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(ts0).TotalMilliseconds, tags);
                AddFirewallRateLimitedEvent(telemetry, session, ip, scope, rateLimit);

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

        private static bool MatchesAny(IpRule[] rules, IPAddress ip) {
            for (int i = 0; i < rules.Length; i++) {
                if (rules[i].Match(ip)) {
                    return true;
                }
            }

            return false;
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

        private static void AddFirewallBlockedEvent(FirewallTelemetry? telemetry, HttpSession session, IPAddress? ip, string reason, string scope) {
            if (telemetry == null) {
                return;
            }

            Activity? activity = Activity.Current;
            if (activity == null) {
                return;
            }

            ActivityTagsCollection tags = CreateEventTags(session, ip, reason, scope);
            activity.AddEvent(new ActivityEvent("firewall.blocked", default, tags));
        }

        private static void AddFirewallRateLimitedEvent(FirewallTelemetry? telemetry, HttpSession session, IPAddress ip, string scope, RateLimitOptions rateLimit) {
            if (telemetry == null) {
                return;
            }

            Activity? activity = Activity.Current;
            if (activity == null) {
                return;
            }

            ActivityTagsCollection tags = CreateEventTags(session, ip, "rate_limited", scope);
            tags.Add("limit", rateLimit.Limit);
            tags.Add("window", rateLimit.Window.ToString());
            tags.Add("window_type", rateLimit.SlidingWindow ? "sliding" : "fixed");
            activity.AddEvent(new ActivityEvent("firewall.rate_limited", default, tags));
        }

        private static ActivityTagsCollection CreateEventTags(HttpSession session, IPAddress? ip, string reason, string scope) {
            return new ActivityTagsCollection() {
                { "reason", reason },
                { "ip", ip?.ToString() ?? string.Empty },
                { "method", session.Request.Method },
                { "path", session.Request.Path },
                { "route", session.Request.RouteTemplate ?? string.Empty },
                { "scope", scope }
            };
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
