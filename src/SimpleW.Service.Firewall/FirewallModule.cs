using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;


namespace SimpleW.Modules {

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
            /// SlidingWindowState
            /// </summary>
            private sealed class SlidingWindowState {
                public readonly ConcurrentQueue<long> Timestamps = new();
                public int Count;
                public long LastSeenTicks;
            }

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

                    IPAddress? ip = _options.ClientIpResolver(session);
                    if (ip == null) {
                        await session.Response.Status(403).SendAsync();
                        return;
                    }

                    // path rule (first match wins)
                    PathRule? pathRule = FindPathRule(session.Request.Path);

                    // deny/allow path rule
                    if (pathRule != null) {
                        if (MatchesAny(pathRule.Deny, ip)) {
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                        if (pathRule.Allow.Count > 0 && !MatchesAny(pathRule.Allow, ip)) {
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                    }
                    // deny/allow global rule
                    else {
                        if (MatchesAny(_options.DenyRules, ip)) {
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                        if (_options.AllowRules.Count > 0 && !MatchesAny(_options.AllowRules, ip)) {
                            await session.Response.Status(403).SendAsync();
                            return;
                        }
                    }

                    // rate limit (path override else global)
                    RateLimitOptions? rl = pathRule?.RateLimit ?? _options.GlobalRateLimit;
                    if (rl != null) {
                        if (IsRateLimited(ip, rl)) {
                            await session.Response.Status(429).SendAsync();
                            return;
                        }
                    }

                    await next();
                });

            }

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

                // trigger cleanup periodically OR if maps grow too large
                if (!fixedOverCap && !slidingOverCap && (n % _options.CleanupEveryNRequests != 0)) {
                    return;
                }

                long minTtl = nowTicks - _options.StateTtl.Ticks;

                // 1) Remove TTL-expired first (cheap + precise)
                if (_fixed.Count > 0) {
                    PurgeExpiredFixed(minTtl, batch: fixedOverCap ? 5000 : 500);
                }
                if (_sliding.Count > 0) {
                    PurgeExpiredSliding(minTtl, batch: slidingOverCap ? 5000 : 500);
                }

                // 2) If still over cap, remove oldest (LRU-ish, best precision)
                if (_fixed.Count > _options.MaxTrackedIps) {
                    PurgeOldestFixed(targetCount: _options.MaxTrackedIps, batch: 3000);
                }
                if (_sliding.Count > _options.MaxTrackedIps) {
                    PurgeOldestSliding(targetCount: _options.MaxTrackedIps, batch: 3000);
                }
            }

            /// <summary>
            /// Remote expired entries
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
            /// Remote expired entries
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
        /// Rate Limit
        /// </summary>
        public RateLimitOptions? RateLimit { get; set; } = null;

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
