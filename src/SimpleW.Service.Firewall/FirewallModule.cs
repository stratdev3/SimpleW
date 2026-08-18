using System.Diagnostics;
using System.Net;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Firewall middleware and runtime instance.
    /// </summary>
    internal sealed class FirewallModule : IHttpModule, IFirewall {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger _log = new Logger<FirewallModule>();

        /// <summary>
        /// Firewall Current Options
        /// </summary>
        private FirewallOptionsSnapshot _options;

        /// <summary>
        /// Rate Limiter
        /// </summary>
        private FirewallRateLimiter _rateLimiter = new();

        /// <summary>
        /// Country Resoluter
        /// </summary>
        private readonly GeoIpCountryResolver _countryResolver = new();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configuration"></param>
        public FirewallModule(FirewallOptionsSnapshot configuration) {
            ArgumentNullException.ThrowIfNull(configuration);
            _options = configuration;
        }

        #region install

        /// <summary>
        /// Installs the middleware into the server pipeline.
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Install(SimpleWServer server) {
            _log.Info("installing...");
            server.UseMiddleware(MiddlewareAsync);
            FirewallModuleExtension.Register(server, this);
            _log.Info("installed");
        }

        /// <summary>
        /// Middleware
        /// </summary>
        /// <param name="session"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        private async ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next) {
            FirewallOptionsSnapshot configuration;
            FirewallRateLimiter rateLimiter;

            lock (_optionsLock) {
                configuration = _options;
                rateLimiter = _rateLimiter;
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

            MaybeCleanup(configuration, rateLimiter);

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
                && rateLimiter.IsRateLimited(ip, rateLimit)) {
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

        #endregion install

        #region update options

        /// <summary>
        /// object lock
        /// </summary>
        private readonly object _optionsLock = new();

        /// <summary>
        /// Atomically updates the current global firewall configuration.
        /// Handler-specific metadata rules are not affected.
        /// </summary>
        /// <param name="update"></param>
        public void Update(Action<FirewallOptions> update) {
            ArgumentNullException.ThrowIfNull(update);

            lock (_optionsLock) {
                FirewallOptionsSnapshot current = _options;
                FirewallOptions options = current.ToOptions();

                update(options);
                options.ValidateAndNormalize();

                ReplaceConfiguration(current, FirewallOptionsSnapshot.FromOptions(options));
            }
        }

        /// <summary>
        /// Replace Current Snapshot Options with new Options
        /// </summary>
        /// <param name="current"></param>
        /// <param name="replacement"></param>
        private void ReplaceConfiguration(FirewallOptionsSnapshot current, FirewallOptionsSnapshot replacement) {
            if (!HasSameRateLimitOptions(current.GlobalPolicy.RateLimit, replacement.GlobalPolicy.RateLimit)) {
                _rateLimiter = new FirewallRateLimiter();
            }

            if (!HasSameCountryResolverOptions(current, replacement)) {
                _countryResolver.Reset();
            }

            Volatile.Write(ref _options, replacement);
        }

        private static bool HasSameRateLimitOptions(RateLimitOptions? current, RateLimitOptions? replacement) {
            if (current == null || replacement == null) {
                return current == null && replacement == null;
            }

            return current.Limit == replacement.Limit
                   && current.Window == replacement.Window
                   && current.SlidingWindow == replacement.SlidingWindow;
        }

        private static bool HasSameCountryResolverOptions(FirewallOptionsSnapshot current, FirewallOptionsSnapshot replacement) {
            return string.Equals(current.MaxMindCountryDbPath, replacement.MaxMindCountryDbPath, StringComparison.Ordinal)
                   && current.EffectiveCountryCacheTtl == replacement.EffectiveCountryCacheTtl;
        }

        #endregion update options

        #region firewall actions

        private void MaybeCleanup(FirewallOptionsSnapshot configuration, FirewallRateLimiter rateLimiter) {
            long n = Interlocked.Increment(ref _requestCounter);

            bool rateOverCap = rateLimiter.IsOverCap(configuration.MaxTrackedIps);
            bool countryOverCap = _countryResolver.IsOverCap(configuration.MaxTrackedIps);

            if (!rateOverCap && !countryOverCap && (n % configuration.CleanupEveryNRequests != 0)) {
                return;
            }

            long nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            rateLimiter.Cleanup(nowTicks, configuration);
            _countryResolver.Cleanup(nowTicks, configuration);
        }

        private static FirewallPolicy ResolvePolicy(HandlerMetadataCollection metadata, FirewallOptionsSnapshot configuration) {
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

        #endregion firewall actions

        #region telemetry

        private FirewallTelemetry? _telemetry;

        private readonly object _telemetryLock = new();

        internal int FixedTrackedIpCount => Volatile.Read(ref _rateLimiter).FixedCount;

        internal int SlidingTrackedIpCount => Volatile.Read(ref _rateLimiter).SlidingCount;

        internal int CountryCacheCount => _countryResolver.CacheCount;

        internal int GlobalAllowRuleCount => Volatile.Read(ref _options).GlobalPolicy.AllowIpRules.Length;

        internal int GlobalDenyRuleCount => Volatile.Read(ref _options).GlobalPolicy.DenyIpRules.Length;

        internal int GlobalAllowCountryRuleCount => Volatile.Read(ref _options).GlobalPolicy.AllowCountryRules.Length;

        internal int GlobalDenyCountryRuleCount => Volatile.Read(ref _options).GlobalPolicy.DenyCountryRules.Length;

        private long _requestCounter;

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

        #endregion telemetry

    }

}
