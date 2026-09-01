using System.Diagnostics.Metrics;


namespace SimpleW.Service.Firewall {

    internal sealed class FirewallTelemetry {

        public readonly Counter<long> DecisionsTotal;
        public readonly Counter<long> BlockedTotal;
        public readonly Counter<long> RateLimitedTotal;
        public readonly Counter<long> AllowMatchTotal;
        public readonly Counter<long> DenyMatchTotal;
        public readonly Counter<long> AllowMissTotal;
        public readonly Histogram<double> DecisionDurationMs;

        public FirewallTelemetry(Meter meter, FirewallModule module) {
            DecisionsTotal = meter.CreateCounter<long>("simplew.firewall.decision.count", unit: "decision");
            BlockedTotal = meter.CreateCounter<long>("simplew.firewall.block.count", unit: "request");
            RateLimitedTotal = meter.CreateCounter<long>("simplew.firewall.ratelimit.count", unit: "request");
            AllowMatchTotal = meter.CreateCounter<long>("simplew.firewall.match.allow.count", unit: "match");
            DenyMatchTotal = meter.CreateCounter<long>("simplew.firewall.match.deny.count", unit: "match");
            AllowMissTotal = meter.CreateCounter<long>("simplew.firewall.match.allow_miss.count", unit: "miss");
            DecisionDurationMs = meter.CreateHistogram<double>("simplew.firewall.decision.duration", unit: "ms");

            meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.fixed", () => module.FixedTrackedIpCount, unit: "ip");
            meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.sliding", () => module.SlidingTrackedIpCount, unit: "ip");
            meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.total", () => module.FixedTrackedIpCount + module.SlidingTrackedIpCount, unit: "ip");
            meter.CreateObservableGauge<int>("simplew.firewall.tracked_ips.country_cache", () => module.CountryCacheCount, unit: "ip");

            meter.CreateObservableGauge<int>("simplew.firewall.rules.allow.global", () => module.GlobalAllowRuleCount, unit: "rule");
            meter.CreateObservableGauge<int>("simplew.firewall.rules.deny.global", () => module.GlobalDenyRuleCount, unit: "rule");
            meter.CreateObservableGauge<int>("simplew.firewall.rules.allow_countries.global", () => module.GlobalAllowCountryRuleCount, unit: "rule");
            meter.CreateObservableGauge<int>("simplew.firewall.rules.deny_countries.global", () => module.GlobalDenyCountryRuleCount, unit: "rule");
        }

    }

}
