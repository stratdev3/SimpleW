using System.Collections.Concurrent;
using System.Net;


namespace SimpleW.Service.Firewall {

    internal sealed class FirewallRateLimiter {

        private readonly ConcurrentDictionary<IPAddress, FixedWindowState> _fixed = new();

        private readonly ConcurrentDictionary<IPAddress, SlidingWindowState> _sliding = new();

        private readonly record struct FixedWindowState(long WindowStartTicks, int Count, long LastSeenTicks);

        private sealed class SlidingWindowState {
            public readonly ConcurrentQueue<long> Timestamps = new();
            public int Count;
            public long LastSeenTicks;
        }

        public int FixedCount => _fixed.Count;

        public int SlidingCount => _sliding.Count;

        public bool IsOverCap(int maxTrackedIps) => _fixed.Count > maxTrackedIps || _sliding.Count > maxTrackedIps;

        public bool IsRateLimited(IPAddress ip, RateLimitOptions rateLimit) {
            long now = DateTimeOffset.UtcNow.UtcTicks;

            if (rateLimit.Window <= TimeSpan.Zero || rateLimit.Limit <= 0) {
                return true;
            }

            return rateLimit.SlidingWindow
                       ? IsSlidingWindowRateLimited(ip, rateLimit, now)
                       : IsFixedWindowRateLimited(ip, rateLimit, now);
        }

        public void Cleanup(long nowTicks, ModuleConfiguration configuration) {
            bool fixedOverCap = _fixed.Count > configuration.MaxTrackedIps;
            bool slidingOverCap = _sliding.Count > configuration.MaxTrackedIps;
            long minTtl = nowTicks - configuration.StateTtl.Ticks;

            if (_fixed.Count > 0) {
                PurgeExpiredFixed(minTtl, fixedOverCap ? 5000 : 500);
            }
            if (_sliding.Count > 0) {
                PurgeExpiredSliding(minTtl, slidingOverCap ? 5000 : 500);
            }

            if (_fixed.Count > configuration.MaxTrackedIps) {
                PurgeOldestFixed(configuration.MaxTrackedIps, 3000);
            }
            if (_sliding.Count > configuration.MaxTrackedIps) {
                PurgeOldestSliding(configuration.MaxTrackedIps, 3000);
            }
        }

        private bool IsFixedWindowRateLimited(IPAddress ip, RateLimitOptions rateLimit, long now) {
            long windowTicks = rateLimit.Window.Ticks;

            while (true) {
                FixedWindowState current = _fixed.GetOrAdd(ip, _ => new FixedWindowState(now, 0, now));

                if (current.LastSeenTicks != now) {
                    FixedWindowState touched = current with { LastSeenTicks = now };
                    if (_fixed.TryUpdate(ip, touched, current)) {
                        current = touched;
                    }
                    else {
                        continue;
                    }
                }

                if (now - current.WindowStartTicks >= windowTicks) {
                    FixedWindowState reset = new(now, 1, now);
                    if (_fixed.TryUpdate(ip, reset, current)) {
                        return false;
                    }
                    continue;
                }

                int nextCount = current.Count + 1;
                FixedWindowState updated = new(current.WindowStartTicks, nextCount, now);
                if (_fixed.TryUpdate(ip, updated, current)) {
                    return nextCount > rateLimit.Limit;
                }
            }
        }

        private bool IsSlidingWindowRateLimited(IPAddress ip, RateLimitOptions rateLimit, long now) {
            SlidingWindowState state = _sliding.GetOrAdd(ip, _ => new SlidingWindowState { LastSeenTicks = now });

            Volatile.Write(ref state.LastSeenTicks, now);

            state.Timestamps.Enqueue(now);
            Interlocked.Increment(ref state.Count);

            long min = now - rateLimit.Window.Ticks;
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

            return finalCount > rateLimit.Limit;
        }

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

        private void PurgeOldestFixed(int targetCount, int batch) {
            while (_fixed.Count > targetCount && batch-- > 0) {
                IPAddress? oldestKey = null;
                long oldestSeen = long.MaxValue;

                int scan = 200;
                foreach (KeyValuePair<IPAddress, FixedWindowState> kv in _fixed) {
                    if (scan-- <= 0) {
                        break;
                    }
                    if (kv.Value.LastSeenTicks < oldestSeen) {
                        oldestSeen = kv.Value.LastSeenTicks;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null) {
                    break;
                }
                _fixed.TryRemove(oldestKey, out _);
            }
        }

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

    }

}
