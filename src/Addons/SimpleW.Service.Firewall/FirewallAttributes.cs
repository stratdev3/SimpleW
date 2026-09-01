namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Marker interface for firewall handler metadata.
    /// </summary>
    public interface IFirewallMetadata : IHandlerMetadata {
    }

    /// <summary>
    /// Declares IP/CIDR rules that are allowed to access the decorated handler.
    /// If at least one allow rule is declared, non-matching requests are denied.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class FirewallAllowIpAttribute : Attribute, IFirewallMetadata {

        /// <summary>
        /// Parsed IP/CIDR rules.
        /// </summary>
        public IReadOnlyList<IpRule> Rules { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="ipOrCidr"></param>
        public FirewallAllowIpAttribute(params string[] ipOrCidr) {
            Rules = FirewallAttributeParser.ParseIpRules(ipOrCidr);
        }

    }

    /// <summary>
    /// Declares IP/CIDR rules that are denied for the decorated handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class FirewallDenyIpAttribute : Attribute, IFirewallMetadata {

        /// <summary>
        /// Parsed IP/CIDR rules.
        /// </summary>
        public IReadOnlyList<IpRule> Rules { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="ipOrCidr"></param>
        public FirewallDenyIpAttribute(params string[] ipOrCidr) {
            Rules = FirewallAttributeParser.ParseIpRules(ipOrCidr);
        }

    }

    /// <summary>
    /// Declares country rules that are allowed to access the decorated handler.
    /// If at least one allow rule is declared, non-matching requests are denied.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class FirewallAllowCountryAttribute : Attribute, IFirewallMetadata {

        /// <summary>
        /// Parsed country rules.
        /// </summary>
        public IReadOnlyList<CountryRule> Rules { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="iso2"></param>
        public FirewallAllowCountryAttribute(params string[] iso2) {
            Rules = FirewallAttributeParser.ParseCountryRules(iso2);
        }

    }

    /// <summary>
    /// Declares country rules that are denied for the decorated handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class FirewallDenyCountryAttribute : Attribute, IFirewallMetadata {

        /// <summary>
        /// Parsed country rules.
        /// </summary>
        public IReadOnlyList<CountryRule> Rules { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="iso2"></param>
        public FirewallDenyCountryAttribute(params string[] iso2) {
            Rules = FirewallAttributeParser.ParseCountryRules(iso2);
        }

    }

    /// <summary>
    /// Allows requests whose country cannot be resolved.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class FirewallAllowUnknownCountryAttribute : Attribute, IFirewallMetadata {
    }

    /// <summary>
    /// Denies requests whose country cannot be resolved.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class FirewallDenyUnknownCountryAttribute : Attribute, IFirewallMetadata {
    }

    /// <summary>
    /// Time unit used by FirewallRateLimitAttribute.
    /// </summary>
    public enum FirewallRateLimitWindowUnit {
        /// <summary>
        /// Milliseconds.
        /// </summary>
        Milliseconds,

        /// <summary>
        /// Seconds.
        /// </summary>
        Seconds,

        /// <summary>
        /// Minutes.
        /// </summary>
        Minutes,

        /// <summary>
        /// Hours.
        /// </summary>
        Hours
    }

    /// <summary>
    /// Declares a rate limit for the decorated handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class FirewallRateLimitAttribute : Attribute, IFirewallMetadata {

        /// <summary>
        /// Request limit.
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// Window amount.
        /// </summary>
        public int Window { get; }

        /// <summary>
        /// Window unit.
        /// </summary>
        public FirewallRateLimitWindowUnit Unit { get; }

        /// <summary>
        /// True for sliding window rate limiting.
        /// </summary>
        public bool SlidingWindow { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="limit"></param>
        /// <param name="window"></param>
        /// <param name="unit"></param>
        /// <param name="slidingWindow"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public FirewallRateLimitAttribute(int limit, int window, FirewallRateLimitWindowUnit unit = FirewallRateLimitWindowUnit.Seconds, bool slidingWindow = false) {
            if (limit <= 0) {
                throw new ArgumentOutOfRangeException(nameof(limit), "Rate limit must be > 0.");
            }
            if (window <= 0) {
                throw new ArgumentOutOfRangeException(nameof(window), "Rate limit window must be > 0.");
            }

            Limit = limit;
            Window = window;
            Unit = unit;
            SlidingWindow = slidingWindow;
        }

        internal RateLimitOptions ToOptions() {
            return new RateLimitOptions {
                Limit = Limit,
                Window = Unit switch {
                    FirewallRateLimitWindowUnit.Milliseconds => TimeSpan.FromMilliseconds(Window),
                    FirewallRateLimitWindowUnit.Seconds => TimeSpan.FromSeconds(Window),
                    FirewallRateLimitWindowUnit.Minutes => TimeSpan.FromMinutes(Window),
                    FirewallRateLimitWindowUnit.Hours => TimeSpan.FromHours(Window),
                    _ => throw new ArgumentOutOfRangeException(nameof(Unit), Unit, "Unsupported rate limit window unit.")
                },
                SlidingWindow = SlidingWindow
            };
        }

    }

    internal static class FirewallAttributeParser {

        public static IpRule[] ParseIpRules(string[] values) {
            if (values == null || values.Length == 0) {
                throw new ArgumentException("At least one IP or CIDR rule must be specified.", nameof(values));
            }

            IpRule[] rules = new IpRule[values.Length];
            for (int i = 0; i < values.Length; i++) {
                rules[i] = IpRule.Parse(values[i]);
            }
            return rules;
        }

        public static CountryRule[] ParseCountryRules(string[] values) {
            if (values == null || values.Length == 0) {
                throw new ArgumentException("At least one country code must be specified.", nameof(values));
            }

            CountryRule[] rules = new CountryRule[values.Length];
            for (int i = 0; i < values.Length; i++) {
                rules[i] = CountryRule.One(values[i]);
            }
            return rules;
        }

    }

}
