namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Immutable runtime configuration used by the firewall.
    /// </summary>
    internal sealed record FirewallOptionsSnapshot(
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

        public static FirewallOptionsSnapshot FromOptions(FirewallOptions options) {
            ArgumentNullException.ThrowIfNull(options);

            return new FirewallOptionsSnapshot(
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

        public FirewallOptions ToOptions() {
            FirewallOptions options = new() {
                GlobalRateLimit = GlobalPolicy.RateLimit?.Clone(),
                StateTtl = StateTtl,
                MaxTrackedIps = MaxTrackedIps,
                CleanupEveryNRequests = CleanupEveryNRequests,
                EnableTelemetry = EnableTelemetry,
                MaxMindCountryDbPath = MaxMindCountryDbPath,
                TreatUnknownCountryAsMatchable = TreatUnknownCountryAsMatchable,
                CountryCacheTtl = CountryCacheTtl
            };

            options.AllowRules.AddRange(GlobalPolicy.AllowIpRules);
            options.DenyRules.AddRange(GlobalPolicy.DenyIpRules);
            options.RateLimitWhitelistRules.AddRange(RateLimitWhitelistRules);
            options.AllowCountries.AddRange(GlobalPolicy.AllowCountryRules);
            options.DenyCountries.AddRange(GlobalPolicy.DenyCountryRules);
            return options;
        }

        public TimeSpan EffectiveCountryCacheTtl => CountryCacheTtl ?? StateTtl;

    }

}
