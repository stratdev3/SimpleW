using System.Net;
using System.Net.Sockets;


namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Rate limit configuration.
    /// </summary>
    public sealed class RateLimitOptions {

        /// <summary>
        /// Maximum number of requests allowed inside the window.
        /// </summary>
        public int Limit { get; set; } = 100;

        /// <summary>
        /// Rate limit window.
        /// </summary>
        public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// False uses a fixed-window counter. True stores timestamps for a sliding window.
        /// </summary>
        public bool SlidingWindow { get; set; }

        internal RateLimitOptions Clone() {
            return new RateLimitOptions {
                Limit = Limit,
                Window = Window,
                SlidingWindow = SlidingWindow
            };
        }

    }

    /// <summary>
    /// Country rule based on ISO2 country codes. Unknown country is represented by null.
    /// </summary>
    public abstract record CountryRule {

        /// <summary>
        /// Creates a rule that matches one ISO2 country code.
        /// </summary>
        /// <param name="iso2"></param>
        /// <returns></returns>
        public static CountryRule One(string iso2) => new OneCountryRule(Norm(iso2));

        /// <summary>
        /// Creates a rule that matches any of the provided ISO2 country codes.
        /// </summary>
        /// <param name="iso2"></param>
        /// <returns></returns>
        public static CountryRule Any(params string[] iso2) => new AnyCountryRule(iso2.Select(Norm).ToArray());

        /// <summary>
        /// Creates a rule that matches unresolved countries.
        /// </summary>
        /// <returns></returns>
        public static CountryRule Unknown() => new UnknownCountryRule();

        /// <summary>
        /// Returns true when the rule matches the resolved country.
        /// </summary>
        /// <param name="iso2"></param>
        /// <returns></returns>
        public abstract bool Match(string? iso2);

        private static string Norm(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                throw new ArgumentException("Country code must not be null or empty.", nameof(value));
            }

            return value.Trim().ToUpperInvariant();
        }

        private sealed record OneCountryRule(string Iso2) : CountryRule {
            public override bool Match(string? iso2) => iso2 != null && string.Equals(iso2, Iso2, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record AnyCountryRule(string[] Iso2) : CountryRule {
            private readonly HashSet<string> _set = new(Iso2, StringComparer.OrdinalIgnoreCase);
            public override bool Match(string? iso2) => iso2 != null && _set.Contains(iso2);
        }

        private sealed record UnknownCountryRule : CountryRule {
            public override bool Match(string? iso2) => iso2 == null;
        }

    }

    /// <summary>
    /// IP rule supporting single IPs and CIDR notation.
    /// </summary>
    public abstract record IpRule {

        /// <summary>
        /// Parses a single IP or CIDR rule.
        /// </summary>
        /// <param name="ipOrCidr"></param>
        /// <returns></returns>
        public static IpRule Parse(string ipOrCidr) {
            if (string.IsNullOrWhiteSpace(ipOrCidr)) {
                throw new ArgumentException("IP rule must not be null or empty.", nameof(ipOrCidr));
            }

            return ipOrCidr.Contains('/', StringComparison.Ordinal)
                       ? Cidr(ipOrCidr)
                       : Single(ipOrCidr);
        }

        /// <summary>
        /// Creates a rule that matches a single IP.
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public static IpRule Single(string ip) => new SingleIpRule(IPAddress.Parse(ip));

        /// <summary>
        /// Creates a rule from CIDR notation.
        /// </summary>
        /// <param name="cidr"></param>
        /// <returns></returns>
        public static IpRule Cidr(string cidr) => CidrRule.ParseCidr(cidr);

        /// <summary>
        /// Returns true when the rule matches the current IP.
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public abstract bool Match(IPAddress ip);

        private sealed record SingleIpRule(IPAddress Ip) : IpRule {
            public override bool Match(IPAddress ip) => ip.Equals(Ip);
        }

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

            public static CidrRule ParseCidr(string cidr) {
                string[] parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2) {
                    throw new ArgumentException($"Invalid CIDR: {cidr}");
                }

                IPAddress ip = IPAddress.Parse(parts[0]);
                if (!int.TryParse(parts[1], out int prefix)) {
                    throw new ArgumentException($"Invalid CIDR prefix: {cidr}");
                }

                int max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
                if (prefix < 0 || prefix > max) {
                    throw new ArgumentOutOfRangeException(nameof(prefix), $"CIDR prefix must be 0..{max}");
                }

                byte[] bytes = ip.GetAddressBytes();
                int fullBytes = prefix / 8;
                int remBits = prefix % 8;

                if (fullBytes < bytes.Length) {
                    if (remBits != 0) {
                        int mask = 0xFF << (8 - remBits);
                        bytes[fullBytes] = (byte)(bytes[fullBytes] & mask);
                        fullBytes++;
                    }

                    for (int i = fullBytes; i < bytes.Length; i++) {
                        bytes[i] = 0;
                    }
                }

                return new CidrRule(new IPAddress(bytes), prefix);
            }

        }

    }

}
