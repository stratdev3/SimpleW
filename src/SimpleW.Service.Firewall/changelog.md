# Changelog


## Unreleased

Maintenance: compatibility with SimpleW v26.1.x.

### feature

- Emit OpenTelemetry events for blocks and rate limits (#395)
- Add `GetFirewall().Update(...)` for atomic runtime updates for global firewall rules and options.

### breakingChange

- Reject repeated `UseFirewallModule(...)` calls. Runtime changes must use `GetFirewall().Update(...)`.



## v26.0.1 - _(2026-06-20)_

Maintenance release for handler-level firewall rules.

### fix

- Fix country deny rules so unresolved countries can be denied through `CountryRule.Unknown()` and `FirewallDenyUnknownCountryAttribute`. (#379)
- Fix country rule evaluation so handler-level country attributes trigger country resolution even when no global country rules are configured.
- Normalize `MaxMindCountryDbPath` and validate firewall configuration before publishing it to the running module.

### feature

- Add handler metadata attributes to declare firewall rules directly on controllers and methods (#383):
  - `FirewallAllowIpAttribute`
  - `FirewallDenyIpAttribute`
  - `FirewallAllowCountryAttribute`
  - `FirewallDenyCountryAttribute`
  - `FirewallAllowUnknownCountryAttribute`
  - `FirewallDenyUnknownCountryAttribute`
  - `FirewallRateLimitAttribute`
- Add `FirewallRateLimitWindowUnit` to configure attribute-based rate-limit windows in milliseconds, seconds, minutes, or hours.
- Allow controller-level and method-level firewall metadata to be combined.
- Allow `UseFirewallModule(...)` to update the firewall configuration while installing the middleware only once per server.

### changed

- Replace path-based firewall overrides with handler metadata rules.
- Handler firewall metadata now replaces global allow/deny/country rules for the decorated handler.
- Handler rate-limit metadata overrides the global rate limit; when no handler rate limit is declared, the global rate limit still applies.
- Move rate limiting, rule evaluation, policy resolution, telemetry, and GeoIP country resolution into dedicated internal components.
- Update documentation and README examples to use handler attributes instead of `PathRule`.

### breakingChange

- `PathRule` / `PathRules` has been removed. Use firewall attributes on controller classes, controller methods, or non-inline delegate methods.
- Static files, fallback routes, and inline lambdas cannot carry C# attributes directly. Protect those routes with global firewall rules, or route them through decorated handlers.



## v26.0.0 - _(2026-04-26)_

Initial release of `SimpleW.Service.Firewall`.

### feature

- Initial `SimpleW.Service.Firewall` package release for SimpleW v26.
- Add IP filtering, rate limiting, connection rules, and security policies to protect SimpleW servers from unwanted or abusive traffic.
