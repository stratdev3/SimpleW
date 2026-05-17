using System.Collections.Concurrent;
using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;


namespace SimpleW.Service.Firewall {

    internal sealed class GeoIpCountryResolver {

        private readonly ConcurrentDictionary<IPAddress, CountryCacheEntry> _countryCache = new();

        private readonly object _readerLock = new();

        private DatabaseReader? _reader;

        private string? _readerPath;

        private readonly record struct CountryCacheEntry(string? Iso2, long ExpiresUtcTicks);

        public int CacheCount => _countryCache.Count;

        public bool IsOverCap(int maxTrackedIps) => _countryCache.Count > maxTrackedIps;

        public string? ResolveCountryIso2(IPAddress ip, ModuleConfiguration configuration) {
            long now = DateTimeOffset.UtcNow.UtcTicks;

            if (_countryCache.TryGetValue(ip, out CountryCacheEntry entry) && entry.ExpiresUtcTicks > now) {
                return entry.Iso2;
            }

            DatabaseReader? reader = GetReader(configuration.MaxMindCountryDbPath);
            string? iso2 = null;

            if (reader != null) {
                try {
                    var response = reader.Country(ip);
                    iso2 = response?.Country?.IsoCode;
                    iso2 = string.IsNullOrWhiteSpace(iso2) ? null : iso2.Trim().ToUpperInvariant();
                }
                catch (AddressNotFoundException) {
                    iso2 = null;
                }
                catch {
                    iso2 = null;
                }
            }

            long expires = now + configuration.EffectiveCountryCacheTtl.Ticks;
            _countryCache[ip] = new CountryCacheEntry(iso2, expires);
            return iso2;
        }

        public void Cleanup(long nowTicks, ModuleConfiguration configuration) {
            bool overCap = _countryCache.Count > configuration.MaxTrackedIps;

            if (_countryCache.Count > 0) {
                PurgeExpired(nowTicks, overCap ? 5000 : 500);
            }

            if (_countryCache.Count > configuration.MaxTrackedIps) {
                PurgeOldest(configuration.MaxTrackedIps, 3000);
            }
        }

        private DatabaseReader? GetReader(string? path) {
            if (string.IsNullOrWhiteSpace(path)) {
                return null;
            }

            DatabaseReader? current = _reader;
            if (current != null && string.Equals(_readerPath, path, StringComparison.Ordinal)) {
                return current;
            }

            lock (_readerLock) {
                if (_reader != null && string.Equals(_readerPath, path, StringComparison.Ordinal)) {
                    return _reader;
                }

                _reader?.Dispose();
                _reader = new DatabaseReader(path);
                _readerPath = path;
                return _reader;
            }
        }

        private void PurgeExpired(long nowTicks, int batch) {
            foreach (KeyValuePair<IPAddress, CountryCacheEntry> kv in _countryCache) {
                if (batch-- <= 0) {
                    break;
                }
                if (kv.Value.ExpiresUtcTicks <= nowTicks) {
                    _countryCache.TryRemove(kv.Key, out _);
                }
            }
        }

        private void PurgeOldest(int targetCount, int batch) {
            while (_countryCache.Count > targetCount && batch-- > 0) {
                IPAddress? oldestKey = null;
                long oldestExpiration = long.MaxValue;

                int scan = 200;
                foreach (KeyValuePair<IPAddress, CountryCacheEntry> kv in _countryCache) {
                    if (scan-- <= 0) {
                        break;
                    }
                    if (kv.Value.ExpiresUtcTicks < oldestExpiration) {
                        oldestExpiration = kv.Value.ExpiresUtcTicks;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null) {
                    break;
                }

                _countryCache.TryRemove(oldestKey, out _);
            }
        }

    }

}
