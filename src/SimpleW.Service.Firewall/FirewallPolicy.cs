using System.Net;


namespace SimpleW.Service.Firewall {

    internal enum FirewallPolicyScope {
        Global,
        Handler
    }

    internal sealed class FirewallPolicy {

        public IpRule[] AllowIpRules { get; }

        public IpRule[] DenyIpRules { get; }

        public CountryRule[] AllowCountryRules { get; }

        public CountryRule[] DenyCountryRules { get; }

        public RateLimitOptions? RateLimit { get; }

        public FirewallPolicyScope Scope { get; }

        public bool IsHandlerPolicy => Scope == FirewallPolicyScope.Handler;

        public string ScopeTag => IsHandlerPolicy ? "handler" : "global";

        public bool HasAllowRules => AllowIpRules.Length > 0 || AllowCountryRules.Length > 0;

        public bool HasCountryRules => AllowCountryRules.Length > 0 || DenyCountryRules.Length > 0;

        private FirewallPolicy(
            FirewallPolicyScope scope,
            IpRule[] allowIpRules,
            IpRule[] denyIpRules,
            CountryRule[] allowCountryRules,
            CountryRule[] denyCountryRules,
            RateLimitOptions? rateLimit
        ) {
            Scope = scope;
            AllowIpRules = allowIpRules;
            DenyIpRules = denyIpRules;
            AllowCountryRules = allowCountryRules;
            DenyCountryRules = denyCountryRules;
            RateLimit = rateLimit;
        }

        public static FirewallPolicy FromGlobal(FirewallModuleExtension.FirewallOptions options) {
            return new FirewallPolicy(
                FirewallPolicyScope.Global,
                options.AllowRules.ToArray(),
                options.DenyRules.ToArray(),
                options.AllowCountries.ToArray(),
                options.DenyCountries.ToArray(),
                options.GlobalRateLimit?.Clone()
            );
        }

        public static bool TryFromMetadata(HandlerMetadataCollection metadata, out FirewallPolicy? policy) {
            IReadOnlyList<IFirewallMetadata> metadataItems = metadata.GetAll<IFirewallMetadata>();
            if (metadataItems.Count == 0) {
                policy = null;
                return false;
            }

            List<IpRule>? allowIpRules = null;
            List<IpRule>? denyIpRules = null;
            List<CountryRule>? allowCountryRules = null;
            List<CountryRule>? denyCountryRules = null;
            RateLimitOptions? rateLimit = null;

            for (int i = 0; i < metadataItems.Count; i++) {
                switch (metadataItems[i]) {
                    case FirewallAllowIpAttribute attr:
                        allowIpRules ??= new List<IpRule>();
                        allowIpRules.AddRange(attr.Rules);
                        break;
                    case FirewallDenyIpAttribute attr:
                        denyIpRules ??= new List<IpRule>();
                        denyIpRules.AddRange(attr.Rules);
                        break;
                    case FirewallAllowCountryAttribute attr:
                        allowCountryRules ??= new List<CountryRule>();
                        allowCountryRules.AddRange(attr.Rules);
                        break;
                    case FirewallDenyCountryAttribute attr:
                        denyCountryRules ??= new List<CountryRule>();
                        denyCountryRules.AddRange(attr.Rules);
                        break;
                    case FirewallAllowUnknownCountryAttribute:
                        allowCountryRules ??= new List<CountryRule>();
                        allowCountryRules.Add(CountryRule.Unknown());
                        break;
                    case FirewallDenyUnknownCountryAttribute:
                        denyCountryRules ??= new List<CountryRule>();
                        denyCountryRules.Add(CountryRule.Unknown());
                        break;
                    case FirewallRateLimitAttribute attr:
                        rateLimit = attr.ToOptions();
                        break;
                }
            }

            policy = new FirewallPolicy(
                FirewallPolicyScope.Handler,
                allowIpRules?.ToArray() ?? Array.Empty<IpRule>(),
                denyIpRules?.ToArray() ?? Array.Empty<IpRule>(),
                allowCountryRules?.ToArray() ?? Array.Empty<CountryRule>(),
                denyCountryRules?.ToArray() ?? Array.Empty<CountryRule>(),
                rateLimit
            );

            return true;
        }

        public bool MatchesDenyIp(IPAddress ip) => MatchesAny(DenyIpRules, ip);

        public bool MatchesAllowIp(IPAddress ip) => MatchesAny(AllowIpRules, ip);

        public bool MatchesDenyCountry(string? iso2, bool treatUnknownAsMatchable) => MatchesAny(DenyCountryRules, iso2, treatUnknownAsMatchable);

        public bool MatchesAllowCountry(string? iso2, bool treatUnknownAsMatchable) => MatchesAny(AllowCountryRules, iso2, treatUnknownAsMatchable);

        private static bool MatchesAny(IpRule[] rules, IPAddress ip) {
            for (int i = 0; i < rules.Length; i++) {
                if (rules[i].Match(ip)) {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesAny(CountryRule[] rules, string? iso2, bool treatUnknownAsMatchable) {
            if (rules.Length == 0) {
                return false;
            }
            if (iso2 == null && !treatUnknownAsMatchable) {
                return false;
            }

            for (int i = 0; i < rules.Length; i++) {
                if (rules[i].Match(iso2)) {
                    return true;
                }
            }

            return false;
        }

    }

}
