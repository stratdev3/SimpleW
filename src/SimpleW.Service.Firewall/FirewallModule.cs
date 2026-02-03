using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Firewall {

    /// <summary>
    /// FirewallModuleExtension
    /// </summary>
    public static class FirewallModuleExtension {

        /// <summary>
        /// Use Firewall Module
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseFirewallModule(this SimpleWServer server, Action<FirewallOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            FirewallOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new FirewallModule(options));
            return server;
        }

        /// <summary>
        /// FirewallOptions
        /// </summary>
        public sealed class FirewallOptions {

            /// <summary>
            /// Client IP Resolver
            /// </summary>
            public Func<HttpSession, IPAddress?> ClientIpResolver { get; set; } = (HttpSession session) => {
                if (session.Socket.RemoteEndPoint is not IPEndPoint ep) {
                    return null;
                }
                return ep.Address;
            };

            /// <summary>
            /// Path-based overrides (first match wins)
            /// </summary>
            public List<PathRule> PathRules { get; } = new();

            /// <summary>
            /// If AllowRules is not empty => default deny for everything not matching allow.
            /// </summary>
            public List<IpRule> AllowRules { get; } = new();

            /// <summary>
            /// DenyRules
            /// </summary>
            public List<IpRule> DenyRules { get; } = new();

            /// <summary>
            /// Global rate limit (null => disabled)
            /// </summary>
            public RateLimitOptions? GlobalRateLimit { get; set; } = null;

            /// <summary>
            /// Inactive IP retention
            /// </summary>
            public TimeSpan StateTtl { get; set; } = TimeSpan.FromMinutes(10);

            /// <summary>
            /// Hard-ish cap (safety)
            /// </summary>
            public int MaxTrackedIps { get; set; } = 50_000;

            /// <summary>
            /// Opportunistic sweep
            /// </summary>
            public int CleanupEveryNRequests { get; set; } = 10_000;

            /// <summary>
            /// Enable Telemetry
            /// (the underlying SimpleWServer.Telemetry must be enabled)
            /// </summary>
            public bool EnableTelemetry { get; set; }

            /// <summary>
            /// Optional MaxMind GeoIP database path (.mmdb) for country lookup.
            /// If null or empty => country rules disabled (treated as unknown).
            /// </summary>
            public string? MaxMindCountryDbPath { get; set; } = null;

            /// <summary>
            /// If false, unresolved country (no db / lookup fail / not found) will never match country rules.
            /// If true, unresolved country can match CountryRule.Unknown().
            /// </summary>
            public bool TreatUnknownCountryAsMatchable { get; set; } = true;

            /// <summary>
            /// Cache resolved IP -> country for this duration.
            /// If null, uses StateTtl.
            /// </summary>
            public TimeSpan? CountryCacheTtl { get; set; } = null;

            /// <summary>
            /// Global allow by country (ISO2 like "FR", "US")
            /// If non-empty => default deny (same behavior as AllowRules)
            /// </summary>
            public List<CountryRule> AllowCountries { get; } = new();

            /// <summary>
            /// Global deny by country (ISO2 like "FR", "US")
            /// </summary>
            public List<CountryRule> DenyCountries { get; } = new();

            /// <summary>
            /// Check Properties and return
            /// </summary>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            public FirewallOptions ValidateAndNormalize() {

                if (StateTtl <= TimeSpan.Zero) {
                    throw new ArgumentException($"{nameof(FirewallOptions)}.{nameof(StateTtl)} must be > 0.", nameof(StateTtl));
                }
                if (MaxTrackedIps <= 0) {
                    throw new ArgumentException($"{nameof(FirewallOptions)}.{nameof(MaxTrackedIps)} must be > 0.", nameof(MaxTrackedIps));
                }
                if (CleanupEveryNRequests <= 0) {
                    throw new ArgumentException($"{nameof(FirewallOptions)}.{nameof(CleanupEveryNRequests)} must be > 0.", nameof(CleanupEveryNRequests));
                }
                if (CountryCacheTtl != null && CountryCacheTtl <= TimeSpan.Zero) {
                    throw new ArgumentException($"{nameof(FirewallOptions)}.{nameof(CountryCacheTtl)} must be > 0.", nameof(CountryCacheTtl));
                }

                return this;
            }

        }

        /// <summary>
        /// FirewallModule
        /// </summary>
        private sealed class FirewallModule : IHttpModule {

            /// <summary>
            /// Options
            /// </summary>
            private readonly FirewallOptions _options;

            /// <summary>
            /// Fixed window: ip -> (windowStartTicks, count)
            /// </summary>
            private readonly ConcurrentDictionary<IPAddress, FixedWindowState> _fixed = new();

            /// <summary>
            /// Sliding window: ip -> queue of ticks
            /// </summary>
            private readonly ConcurrentDictionary<IPAddress, SlidingWindowState> _sliding = new();

            /// <summary>
            /// Country cache: ip -> (iso2, expiresUtcTicks)
            /// </summary>
            private readonly ConcurrentDictionary<IPAddress, CountryCacheEntry> _countryCache = new();

            /// <summary>
            /// Request Counter
            /// </summary>
            private long _requestCounter;

            /// <summary>
            /// FixedWindowState
            /// </summary>
            /// <param name="WindowStartTicks"></param>
            /// <param name="Count"></param>
            /// <param name="LastSeenTicks"></param>
            private readonly record struct FixedWindowState(long WindowStartTicks, int Count, long LastSeenTicks);

            /// <summary>
            /// CountryCacheEntry
            /// </summary>
            /// <param name="Iso2"></param>
            /// <param name="ExpiresUtcTicks"></param>
            private readonly record struct CountryCacheEntry(string? Iso2, long ExpiresUtcTicks);

            /// <summary>
            /// SlidingWindowState
            /// </summary>
            private sealed class SlidingWindowState {
                public readonly ConcurrentQueue<long> Timestamps = new();
                public int Count;
                public long LastSeenTicks;
            }

            /// <summary>
            /// MaxMind reader (lazy)
            /// </summary>
            private DatabaseReader? _geoReader;

            /// <summary>
            /// Lock
            /// </summary>
            private readonly object _geoLock = new();

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="options"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public FirewallModule(FirewallOptions options) {
                _options = options.ValidateAndNormalize() ?? throw new ArgumentNullException(nameof(options));
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("FirewallModule must be installed before server start.");
                }

                // sort path rules : longest prefix first => most specific wins
                _options.PathRules.Sort(static (a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));

                server.UseMiddleware(async (session, next) => {

                    long _ts0 = Stopwatch.GetTimestamp();
                    FirewallTelemetry? _mt = EnsureTelemetry(_options.EnableTelemetry, session.Server);

                    IPAddress? ip = _options.ClientIpResolver(session);
                    if (ip == null) {
                        if (_mt != null) {
                            TagList tags = default;
                            tags.Add("result", "blocked");
                            tags.Add("reason", "no_ip");
                            tags.Add("scope", "global");
                            _mt.DecisionsTotal.Add(1, tags);
                            _mt.BlockedTotal.Add(1, tags);
                            _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                        }
                        await session.Response.Status(403).SendAsync();
                        return;
                    }

                    // Resolve country ISO2 (ex: "FR") from MaxMind (may be null => unknown)
                    string? countryIso2 = (_options.AllowCountries.Count > 0 || _options.DenyCountries.Count > 0) ? ResolveCountryIso2(ip) : null;

                    // path rule (first match wins)
                    PathRule? pathRule = FindPathRule(session.Request.Path);

                    // deny/allow path rule
                    if (pathRule != null) {

                        // deny by IP
                        if (MatchesAny(pathRule.Deny, ip)) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "blocked");
                                tags.Add("reason", "path_deny");
                                tags.Add("scope", "path");
                                tags.Add("path_prefix", pathRule.Prefix);
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.BlockedTotal.Add(1, tags);
                                TagList mtags = default;
                                mtags.Add("scope", "path");
                                mtags.Add("path_prefix", pathRule.Prefix);
                                _mt.DenyMatchTotal.Add(1, mtags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(403).SendAsync();
                            return;
                        }

                        // deny by country
                        if (MatchesAnyCountry(pathRule.DenyCountries, countryIso2, _options.TreatUnknownCountryAsMatchable)) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "blocked");
                                tags.Add("reason", "path_country_deny");
                                tags.Add("scope", "path");
                                tags.Add("path_prefix", pathRule.Prefix);
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.BlockedTotal.Add(1, tags);
                                TagList mtags = default;
                                mtags.Add("scope", "path");
                                mtags.Add("path_prefix", pathRule.Prefix);
                                _mt.DenyMatchTotal.Add(1, mtags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(403).SendAsync();
                            return;
                        }

                        bool mustMatchAllow = (pathRule.Allow.Count > 0) || (pathRule.AllowCountries.Count > 0);
                        bool allowMatched = (pathRule.Allow.Count > 0 && MatchesAny(pathRule.Allow, ip))
                                            || (pathRule.AllowCountries.Count > 0 && MatchesAnyCountry(pathRule.AllowCountries, countryIso2, _options.TreatUnknownCountryAsMatchable));

                        if (mustMatchAllow && !allowMatched) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "blocked");
                                tags.Add("reason", "path_allow_miss");
                                tags.Add("scope", "path");
                                tags.Add("path_prefix", pathRule.Prefix);
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.BlockedTotal.Add(1, tags);
                                TagList mtags = default;
                                mtags.Add("scope", "path");
                                mtags.Add("path_prefix", pathRule.Prefix);
                                _mt.AllowMissTotal.Add(1, mtags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                        if (mustMatchAllow && allowMatched && _mt != null) {
                            TagList mtags = default;
                            mtags.Add("scope", "path");
                            mtags.Add("path_prefix", pathRule.Prefix);
                            _mt.AllowMatchTotal.Add(1, mtags);
                        }
                    }
                    // deny/allow global rule
                    else {
                        // deny by IP
                        if (MatchesAny(_options.DenyRules, ip)) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "blocked");
                                tags.Add("reason", "global_deny");
                                tags.Add("scope", "global");
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.BlockedTotal.Add(1, tags);
                                TagList mtags = default;
                                mtags.Add("scope", "global");
                                _mt.DenyMatchTotal.Add(1, mtags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                        // deny by country
                        if (MatchesAnyCountry(_options.DenyCountries, countryIso2, _options.TreatUnknownCountryAsMatchable)) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "blocked");
                                tags.Add("reason", "global_country_deny");
                                tags.Add("scope", "global");
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.BlockedTotal.Add(1, tags);
                                TagList mtags = default;
                                mtags.Add("scope", "global");
                                _mt.DenyMatchTotal.Add(1, mtags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(403).SendAsync();
                            return;
                        }


                        bool mustMatchGlobalAllow = (_options.AllowRules.Count > 0) || (_options.AllowCountries.Count > 0);
                        bool globalAllowMatched = (_options.AllowRules.Count > 0 && MatchesAny(_options.AllowRules, ip))
                                                   || (_options.AllowCountries.Count > 0 && MatchesAnyCountry(_options.AllowCountries, countryIso2, _options.TreatUnknownCountryAsMatchable));

                        if (mustMatchGlobalAllow && !globalAllowMatched) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "blocked");
                                tags.Add("reason", "global_allow_miss");
                                tags.Add("scope", "global");
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.BlockedTotal.Add(1, tags);
                                TagList mtags = default;
                                mtags.Add("scope", "global");
                                _mt.AllowMissTotal.Add(1, mtags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                        if (mustMatchGlobalAllow && globalAllowMatched && _mt != null) {
                            TagList mtags = default;
                            mtags.Add("scope", "global");
                            _mt.AllowMatchTotal.Add(1, mtags);
                        }
                    }

                    // rate limit (path override else global)
                    RateLimitOptions? rl = pathRule?.RateLimit ?? _options.GlobalRateLimit;
                    if (rl != null) {
                        if (IsRateLimited(ip, rl)) {
                            if (_mt != null) {
                                TagList tags = default;
                                tags.Add("result", "rate_limited");
                                tags.Add("reason", "rate_limited");
                                tags.Add("scope", pathRule != null ? "path" : "global");
                                tags.Add("window", rl.SlidingWindow ? "sliding" : "fixed");
                                if (pathRule != null) {
                                    tags.Add("path_prefix", pathRule.Prefix);
                                }
                                _mt.DecisionsTotal.Add(1, tags);
                                _mt.RateLimitedTotal.Add(1, tags);
                                _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                            }
                            await session.Response.Status(429).SendAsync();
                            return;
                        }
                    }

                    if (_mt != null) {
                        TagList tags = default;
                        tags.Add("result", "allowed");
                        tags.Add("reason", "ok");
                        tags.Add("scope", pathRule != null ? "path" : "global");
                        if (pathRule != null) {
                            tags.Add("path_prefix", pathRule.Prefix);
                        }
                        _mt.DecisionsTotal.Add(1, tags);
                        _mt.DecisionDurationMs.Record(Stopwatch.GetElapsedTime(_ts0).TotalMilliseconds, tags);
                    }

                    await next();
                });

            }

            #region telemetry

            /// <summary>
            /// Telemtry (lazy)
            /// </summary>
            private FirewallTelemetry? _telemetry;

            /// <summary>
            /// Telemtry Lock
            /// </summary>
            private readonly object _telemetryLock = new();

            /// <summary>
            /// Set Firewall Telemetry dependings the underlying server.Telemetry
            /// </summary>
            /// <param name="enable"></param>
            /// <param name="server"></param>
            /// <returns></returns>
            private FirewallTelemetry? EnsureTelemetry(bool enable, SimpleWServer server) {
                if (!enable) {
                    return null;
                }
                Telemetry? telemetry = server.Telemetry;
                if (telemetry == null || !server.IsTelemetryEnabled) {
                    return null;
                }
                FirewallTelemetry? t = _telemetry;
                if (t != null) {
                    return t;
                }
                lock (_telemetryLock) {
                    _telemetry ??= new FirewallTelemetry(telemetry.Meter, this);
                    return _telemetry;
                }
            }

            /// <summary>
            /// FirewallTelemetry
            /// </summary>
            private class FirewallTelemetry {

                public readonly Counter<long> DecisionsTotal;
                public readonly Counter<long> BlockedTotal;
                public readonly Counter<long> RateLimitedTotal;
                public readonly Counter<long> AllowMatchTotal;
                public readonly Counter<long> DenyMatchTotal;
                public readonly Counter<long> AllowMissTotal;
                public readonly Histogram<double> DecisionDurationMs;

                /// <summary>
                /// Constructor
                /// </summary>
                /// <param name="meter"></param>
                /// <param name="module"></param>
                public FirewallTelemetry(Meter meter, FirewallModule module) {
                    DecisionsTotal = meter.CreateCounter<long>("simplew.firewall.decision.count", unit: "decision");
                    BlockedTotal = meter.CreateCounter<long>("simplew.firewall.block.count", unit: "request");
                    RateLimitedTotal = meter.CreateCounter<long>("simplew.firewall.ratelimit.count", unit: "request");
                    AllowMatchTotal = meter.CreateCounter<long>("simplew.firewall.match.allow.count", unit: "match");
                    DenyMatchTotal = meter.CreateCounter<long>("simplew.firewall.match.deny.count", unit: "match");
                    AllowMissTotal = meter.CreateCounter<long>("simplew.firewall.match.allow_miss.count", unit: "miss");
                    DecisionDurationMs = meter.CreateHistogram<double>("simplew.firewall.decision.duration", unit: "ms");

                    // Gauges: keep low-cardinality (no IPs / no per-path labels here)
                    meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.fixed", () => module._fixed.Count, unit: "ip");
                    meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.sliding", () => module._sliding.Count, unit: "ip");
                    meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.total", () => module._fixed.Count + module._sliding.Count, unit: "ip");
                    meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.country_cache", () => module._countryCache.Count, unit: "ip");

                    meter.CreateObservableGauge<int>("simplew.firewall.rules.paths", () => module._options.PathRules.Count, unit: "rule");
                    meter.CreateObservableGauge<int>("simplew.firewall.rules.allow.global", () => module._options.AllowRules.Count, unit: "rule");
                    meter.CreateObservableGauge<int>("simplew.firewall.rules.deny.global", () => module._options.DenyRules.Count, unit: "rule");
                    meter.CreateObservableGauge<int>("simplew.firewall.rules.allow_countries.global", () => module._options.AllowCountries.Count, unit: "rule");
                    meter.CreateObservableGauge<int>("simplew.firewall.rules.deny_countries.global", () => module._options.DenyCountries.Count, unit: "rule");
                }
            }

            #endregion telemetry

            #region helpers

            /// <summary>
            /// FindPathRule
            /// </summary>
            /// <param name="path"></param>
            /// <returns></returns>
            private PathRule? FindPathRule(string path) {
                for (int i = 0; i < _options.PathRules.Count; i++) {
                    PathRule r = _options.PathRules[i];
                    if (path.StartsWith(r.Prefix, StringComparison.Ordinal)) {
                        return r;
                    }
                }
                return null;
            }

            /// <summary>
            /// MatchesAny
            /// </summary>
            /// <param name="rules"></param>
            /// <param name="ip"></param>
            /// <returns></returns>
            private static bool MatchesAny(List<IpRule> rules, IPAddress ip) {
                for (int i = 0; i < rules.Count; i++) {
                    if (rules[i].Match(ip)) {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// MatchesAnyCountry
            /// </summary>
            /// <param name="rules"></param>
            /// <param name="iso2"></param>
            /// <param name="treatUnknownAsMatchable"></param>
            /// <returns></returns>
            private static bool MatchesAnyCountry(List<CountryRule> rules, string? iso2, bool treatUnknownAsMatchable) {
                if (rules.Count == 0) {
                    return false;
                }
                if (iso2 == null && !treatUnknownAsMatchable) {
                    return false;
                }
                for (int i = 0; i < rules.Count; i++) {
                    if (rules[i].Match(iso2)) {
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// GetGeoReader (lazy)
            /// </summary>
            /// <returns></returns>
            private DatabaseReader? GetGeoReader() {
                string? path = _options.MaxMindCountryDbPath;
                if (string.IsNullOrWhiteSpace(path)) {
                    return null;
                }

                DatabaseReader? r = _geoReader;
                if (r != null) {
                    return r;
                }

                lock (_geoLock) {
                    if (_geoReader != null) {
                        return _geoReader;
                    }
                    _geoReader = new DatabaseReader(path);
                    return _geoReader;
                }
            }

            /// <summary>
            /// Resolve country ISO2 for IP using MaxMind (Country DB)
            /// Returns null if unknown/unresolved/no DB.
            /// </summary>
            /// <param name="ip"></param>
            /// <returns></returns>
            private string? ResolveCountryIso2(IPAddress ip) {
                DatabaseReader? reader = GetGeoReader();
                if (reader == null) {
                    return null;
                }

                long now = DateTimeOffset.UtcNow.UtcTicks;
                TimeSpan ttl = _options.CountryCacheTtl ?? _options.StateTtl;
                long exp = now + ttl.Ticks;

                if (_countryCache.TryGetValue(ip, out CountryCacheEntry entry) && entry.ExpiresUtcTicks > now) {
                    return entry.Iso2;
                }

                string? iso2 = null;
                try {
                    var resp = reader.Country(ip);
                    iso2 = resp?.Country?.IsoCode;
                    if (!string.IsNullOrWhiteSpace(iso2)) {
                        iso2 = iso2.Trim().ToUpperInvariant();
                    }
                    else {
                        iso2 = null;
                    }
                }
                catch (AddressNotFoundException) {
                    iso2 = null;
                }
                catch {
                    // Fail-safe: firewall must not crash because geo DB is unhappy
                    iso2 = null;
                }

                _countryCache[ip] = new CountryCacheEntry(iso2, exp);
                return iso2;
            }

            /// <summary>
            /// IsRateLimited
            /// </summary>
            /// <param name="ip"></param>
            /// <param name="rl"></param>
            /// <returns></returns>
            private bool IsRateLimited(IPAddress ip, RateLimitOptions rl) {
                long now = DateTimeOffset.UtcNow.UtcTicks;
                MaybeCleanup(now);

                // fail-closed on invalid config
                if (rl.Window <= TimeSpan.Zero || rl.Limit <= 0) {
                    return true;
                }

                // fix window state
                if (!rl.SlidingWindow) {
                    long windowTicks = rl.Window.Ticks;

                    while (true) {
                        FixedWindowState current = _fixed.GetOrAdd(ip, _ => new FixedWindowState(now, 0, now));

                        if (current.LastSeenTicks != now) {
                            var touched = current with { LastSeenTicks = now };
                            if (_fixed.TryUpdate(ip, touched, current)) {
                                current = touched;
                            }
                            else {
                                continue;
                            }
                        }

                        // if window expired => reset
                        if (now - current.WindowStartTicks >= windowTicks) {
                            FixedWindowState reset = new(now, 1, now);
                            if (_fixed.TryUpdate(ip, reset, current)) {
                                return false;
                            }
                            continue;
                        }

                        // same window => increment
                        int nextCount = current.Count + 1;
                        FixedWindowState updated = new(current.WindowStartTicks, nextCount, now);
                        if (_fixed.TryUpdate(ip, updated, current)) {
                            return nextCount > rl.Limit;
                        }
                    }
                }
                // sliding window state
                else {
                    var state = _sliding.GetOrAdd(ip, _ => new SlidingWindowState { LastSeenTicks = now });

                    // touch last seen
                    Volatile.Write(ref state.LastSeenTicks, now);

                    // add current request
                    state.Timestamps.Enqueue(now);
                    Interlocked.Increment(ref state.Count);

                    long min = now - rl.Window.Ticks;

                    // evict expired timestamps
                    while (state.Timestamps.TryPeek(out long head) && head < min) {
                        if (state.Timestamps.TryDequeue(out _)) {
                            Interlocked.Decrement(ref state.Count);
                        }
                    }

                    int finalCount = Volatile.Read(ref state.Count);
                    if (finalCount < 0) {
                        Interlocked.Exchange(ref state.Count, 0);
                        finalCount = 0;
                    }

                    return finalCount > rl.Limit;
                }
            }

            /// <summary>
            /// MaybeCleanup
            /// </summary>
            /// <param name="nowTicks"></param>
            private void MaybeCleanup(long nowTicks) {
                long n = Interlocked.Increment(ref _requestCounter);

                bool fixedOverCap = _fixed.Count > _options.MaxTrackedIps;
                bool slidingOverCap = _sliding.Count > _options.MaxTrackedIps;
                bool countryOverCap = _countryCache.Count > _options.MaxTrackedIps;

                // trigger cleanup periodically OR if maps grow too large
                if (!fixedOverCap && !slidingOverCap && !countryOverCap && (n % _options.CleanupEveryNRequests != 0)) {
                    return;
                }

                long minTtl = nowTicks - _options.StateTtl.Ticks;
                long minCountryTtl = nowTicks - (_options.CountryCacheTtl ?? _options.StateTtl).Ticks;

                // 1) Remove TTL-expired first (cheap + precise)
                if (_fixed.Count > 0) {
                    PurgeExpiredFixed(minTtl, batch: fixedOverCap ? 5000 : 500);
                }
                if (_sliding.Count > 0) {
                    PurgeExpiredSliding(minTtl, batch: slidingOverCap ? 5000 : 500);
                }
                if (_countryCache.Count > 0) {
                    PurgeExpiredCountry(minCountryTtl, batch: countryOverCap ? 5000 : 500);
                }

                // 2) If still over cap, remove oldest (LRU-ish, best precision)
                if (_fixed.Count > _options.MaxTrackedIps) {
                    PurgeOldestFixed(targetCount: _options.MaxTrackedIps, batch: 3000);
                }
                if (_sliding.Count > _options.MaxTrackedIps) {
                    PurgeOldestSliding(targetCount: _options.MaxTrackedIps, batch: 3000);
                }
                if (_countryCache.Count > _options.MaxTrackedIps) {
                    PurgeOldestCountry(targetCount: _options.MaxTrackedIps, batch: 3000);
                }
            }

            /// <summary>
            /// Remove expired entries
            /// </summary>
            /// <param name="minTtlTicks"></param>
            /// <param name="batch"></param>
            private void PurgeExpiredFixed(long minTtlTicks, int batch) {
                foreach (KeyValuePair<IPAddress, FixedWindowState> kv in _fixed) {
                    if (batch-- <= 0) {
                        break;
                    }
                    if (kv.Value.LastSeenTicks < minTtlTicks) {
                        _fixed.TryRemove(kv.Key, out _);
                    }
                }
            }

            /// <summary>
            /// Remove expired entries
            /// </summary>
            /// <param name="minTtlTicks"></param>
            /// <param name="batch"></param>
            private void PurgeExpiredSliding(long minTtlTicks, int batch) {
                foreach (KeyValuePair<IPAddress, SlidingWindowState> kv in _sliding) {
                    if (batch-- <= 0) {
                        break;
                    }
                    long last = Volatile.Read(ref kv.Value.LastSeenTicks);
                    if (last < minTtlTicks) {
                        _sliding.TryRemove(kv.Key, out _);
                    }
                }
            }

            /// <summary>
            /// Remove expired entries
            /// </summary>
            /// <param name="minTtlTicks"></param>
            /// <param name="batch"></param>
            private void PurgeExpiredCountry(long minTtlTicks, int batch) {
                foreach (KeyValuePair<IPAddress, CountryCacheEntry> kv in _countryCache) {
                    if (batch-- <= 0) {
                        break;
                    }
                    if (kv.Value.ExpiresUtcTicks < minTtlTicks) {
                        _countryCache.TryRemove(kv.Key, out _);
                    }
                }
            }

            /// <summary>
            /// Remove oldest entries
            /// </summary>
            /// <param name="targetCount"></param>
            /// <param name="batch"></param>
            private void PurgeOldestFixed(int targetCount, int batch) {
                // We approximate "oldest" by scanning a limited budget and removing the smallest LastSeen.
                while (_fixed.Count > targetCount && batch-- > 0) {

                    IPAddress? oldestKey = null;
                    long oldestSeen = long.MaxValue;

                    int scan = 200; // small scan window
                    foreach (KeyValuePair<IPAddress, FixedWindowState> kv in _fixed) {
                        if (scan-- <= 0) {
                            break;
                        }
                        long seen = kv.Value.LastSeenTicks;
                        if (seen < oldestSeen) {
                            oldestSeen = seen;
                            oldestKey = kv.Key;
                        }
                    }

                    if (oldestKey == null) {
                        break;
                    }
                    _fixed.TryRemove(oldestKey, out _);
                }
            }

            /// <summary>
            /// Remove oldest entries
            /// </summary>
            /// <param name="targetCount"></param>
            /// <param name="batch"></param>
            private void PurgeOldestSliding(int targetCount, int batch) {
                while (_sliding.Count > targetCount && batch-- > 0) {

                    IPAddress? oldestKey = null;
                    long oldestSeen = long.MaxValue;

                    int scan = 200;
                    foreach (KeyValuePair<IPAddress, SlidingWindowState> kv in _sliding) {
                        if (scan-- <= 0) {
                            break;
                        }
                        long seen = Volatile.Read(ref kv.Value.LastSeenTicks);
                        if (seen < oldestSeen) {
                            oldestSeen = seen;
                            oldestKey = kv.Key;
                        }
                    }

                    if (oldestKey == null) {
                        break;
                    }
                    _sliding.TryRemove(oldestKey, out _);
                }
            }

            /// <summary>
            /// Remove oldest entries
            /// </summary>
            /// <param name="targetCount"></param>
            /// <param name="batch"></param>
            private void PurgeOldestCountry(int targetCount, int batch) {
                while (_countryCache.Count > targetCount && batch-- > 0) {

                    IPAddress? oldestKey = null;
                    long oldestExp = long.MaxValue;

                    int scan = 200;
                    foreach (KeyValuePair<IPAddress, CountryCacheEntry> kv in _countryCache) {
                        if (scan-- <= 0) {
                            break;
                        }
                        long exp = kv.Value.ExpiresUtcTicks;
                        if (exp < oldestExp) {
                            oldestExp = exp;
                            oldestKey = kv.Key;
                        }
                    }

                    if (oldestKey == null) {
                        break;
                    }
                    _countryCache.TryRemove(oldestKey, out _);
                }
            }

            #endregion helpers

        }

    }

    #region models

    /// <summary>
    /// RateLimitOptions
    /// </summary>
    public sealed class RateLimitOptions {

        /// <summary>
        /// Request Limit
        /// </summary>
        public int Limit { get; set; } = 100;

        /// <summary>
        /// Time Window
        /// </summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// If true, sliding-ish (we store timestamps). If false, fixed window counter.
        /// </summary>
        public bool SlidingWindow { get; set; } = false;

    }

    /// <summary>
    /// PathRule
    /// </summary>
    public sealed class PathRule {

        /// <summary>
        /// normalized prefix
        /// </summary>
        public string Prefix { get; set; } = "/";

        /// <summary>
        /// Allow Rules
        /// </summary>
        public List<IpRule> Allow { get; } = new();

        /// <summary>
        /// Deny Rules
        /// </summary>
        public List<IpRule> Deny { get; } = new();

        /// <summary>
        /// Allow Countries (ISO2 like "FR", "US")
        /// If non-empty => default deny for this path if no match (same as Allow)
        /// </summary>
        public List<CountryRule> AllowCountries { get; } = new();

        /// <summary>
        /// Deny Countries (ISO2 like "FR", "US")
        /// </summary>
        public List<CountryRule> DenyCountries { get; } = new();

        /// <summary>
        /// Rate Limit
        /// </summary>
        public RateLimitOptions? RateLimit { get; set; } = null;

    }

    /// <summary>
    /// CountryRule (ISO2 codes, case-insensitive).
    /// Unknown country is represented by iso2 == null.
    /// </summary>
    public abstract record CountryRule {

        /// <summary>
        /// One
        /// </summary>
        /// <param name="iso2"></param>
        /// <returns></returns>
        public static CountryRule One(string iso2) => new OneCountryRule(Norm(iso2));

        /// <summary>
        /// Any
        /// </summary>
        /// <param name="iso2"></param>
        /// <returns></returns>
        public static CountryRule Any(params string[] iso2) => new AnyCountryRule(iso2.Select(Norm).ToArray());

        /// <summary>
        /// Matches when the country is unknown (no db / not found / lookup fail).
        /// </summary>
        public static CountryRule Unknown() => new UnknownCountryRule();

        /// <summary>
        /// Match
        /// </summary>
        /// <param name="iso2"></param>
        /// <returns></returns>
        public abstract bool Match(string? iso2);

        private static string Norm(string s) => s.Trim().ToUpperInvariant();

        private sealed record OneCountryRule(string Iso2) : CountryRule {
            public override bool Match(string? iso2) => iso2 != null && string.Equals(iso2, Iso2, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record AnyCountryRule(string[] Iso2) : CountryRule {
            private readonly HashSet<string> _set = new(Iso2, StringComparer.OrdinalIgnoreCase);
            public override bool Match(string? iso2) => iso2 != null && _set.Contains(iso2);
        }

        private sealed record UnknownCountryRule() : CountryRule {
            public override bool Match(string? iso2) => iso2 == null;
        }

    }

    /// <summary>
    /// IpRule
    /// </summary>
    public abstract record IpRule {

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public static IpRule Single(string ip) => new SingleIpRule(IPAddress.Parse(ip));

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="cidr"></param>
        /// <returns></returns>
        public static IpRule Cidr(string cidr) => CidrRule.Parse(cidr);

        /// <summary>
        /// SingleIpRule
        /// </summary>
        /// <param name="Ip"></param>
        private sealed record SingleIpRule(IPAddress Ip) : IpRule {
            public override bool Match(IPAddress ip) => ip.Equals(Ip);
        }

        /// <summary>
        /// Match
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public abstract bool Match(IPAddress ip);

        /// <summary>
        /// CidrRule
        /// </summary>
        /// <param name="Network"></param>
        /// <param name="PrefixLength"></param>
        private sealed record CidrRule(IPAddress Network, int PrefixLength) : IpRule {

            public override bool Match(IPAddress ip) {
                if (ip.AddressFamily != Network.AddressFamily) {
                    return false;
                }

                byte[] net = Network.GetAddressBytes();
                byte[] addr = ip.GetAddressBytes();

                int fullBytes = PrefixLength / 8;
                int remBits = PrefixLength % 8;

                for (int i = 0; i < fullBytes; i++) {
                    if (addr[i] != net[i]) {
                        return false;
                    }
                }

                if (remBits == 0) {
                    return true;
                }

                int mask = 0xFF << (8 - remBits);
                return (addr[fullBytes] & mask) == (net[fullBytes] & mask);
            }

            public static CidrRule Parse(string cidr) {
                var parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) {
                    throw new ArgumentException($"Invalid CIDR: {cidr}");
                }

                var ip = IPAddress.Parse(parts[0]);
                if (!int.TryParse(parts[1], out int prefix)) {
                    throw new ArgumentException($"Invalid CIDR prefix: {cidr}");
                }

                int max = (ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
                if (prefix < 0 || prefix > max) {
                    throw new ArgumentOutOfRangeException(nameof(prefix), $"CIDR prefix must be 0..{max}");
                }

                // Normalize network address (zero host bits)
                byte[] b = ip.GetAddressBytes();
                int fullBytes = prefix / 8;
                int remBits = prefix % 8;

                if (fullBytes < b.Length) {
                    if (remBits != 0) {
                        int mask = 0xFF << (8 - remBits);
                        b[fullBytes] = (byte)(b[fullBytes] & mask);
                        fullBytes++;
                    }
                    for (int i = fullBytes; i < b.Length; i++) {
                        b[i] = 0;
                    }
                }

                return new CidrRule(new IPAddress(b), prefix);
            }

        }

    }

    #endregion models

}
