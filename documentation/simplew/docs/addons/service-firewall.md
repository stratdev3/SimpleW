# Firewall <Badge type="tip" text="Official" />

The [`SimpleW.Service.Firewall`](https://www.nuget.org/packages/SimpleW.Service.Firewall) package provides an application-level firewall module for SimpleW. It runs as middleware and reads SimpleW handler metadata for route-specific rules.


## Features

- Allow or deny requests by client IP or CIDR
- Allow or deny requests by client country with optional MaxMind GeoIP2
- Configure global rules at installation and update them while the server is running
- Declare handler-specific rules with attributes
- Apply fixed-window or sliding-window rate limits
- Bypass rate limits for trusted IP/CIDR clients
- Clean internal per-IP state with TTL and hard caps
- Emit optional telemetry


## Requirements

- .NET 8.0
- SimpleW (core server)


## Installation

```sh
dotnet add package SimpleW.Service.Firewall
```

See the [changelog](./service-firewall-changelog.md)


## Configuration options

| Option | Default | Description |
|---|---:|---|
| `AllowRules` | `[]` | Global allow list by IP/CIDR. If not empty, non-matching requests are denied. |
| `DenyRules` | `[]` | Global deny list by IP/CIDR. |
| `GlobalRateLimit` | `null` | Global rate limit policy. |
| `RateLimitWhitelistRules` | `[]` | IP/CIDR rules that bypass global and handler rate limits without bypassing allow or deny rules. |
| `StateTtl` | `10 minutes` | Retention for inactive per-IP state. |
| `MaxTrackedIps` | `50000` | Safety cap for tracked IP entries. |
| `CleanupEveryNRequests` | `10000` | Opportunistic cleanup frequency. |
| `EnableTelemetry` | `false` | Enables firewall metrics when server telemetry is enabled too. |
| `MaxMindCountryDbPath` | `null` | Optional MaxMind `.mmdb` country database path. |
| `TreatUnknownCountryAsMatchable` | `true` | Lets unknown countries match `CountryRule.Unknown()` and unknown-country attributes. |
| `CountryCacheTtl` | `null` | IP to country cache duration. Uses `StateTtl` when null. |
| `AllowCountries` | `[]` | Global allow list by ISO2 country. If not empty, non-matching requests are denied. |
| `DenyCountries` | `[]` | Global deny list by ISO2 country. |


## Minimal example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Firewall;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseFirewallModule(options => {
    options.AllowRules.Add(IpRule.Cidr("10.0.0.0/8"));
    options.AllowRules.Add(IpRule.Single("127.0.0.1"));
});

server.MapController<ApiController>("/api");

await server.RunAsync();

[Route("/admin")]
public sealed class ApiController : Controller
{
    [FirewallAllowIp("192.168.1.0/24")]
    [FirewallRateLimit(20, 10, FirewallRateLimitWindowUnit.Seconds)]
    [Route("GET", "/dashboard")]
    public object Dashboard()
    {
        return new { ok = true };
    }
}
```


## Runtime Updates

Install the firewall once with `UseFirewallModule(...)`. After that, including after the server has started, retrieve the attached runtime instance with `GetFirewall()` and update its global options:

```csharp
IFirewall firewall = server.GetFirewall();
IpRule temporaryBlock = IpRule.Single("203.0.113.10");

firewall.Update(options => {
    options.DenyRules.Add(temporaryBlock);
});

firewall.Update(options => {
    options.DenyRules.RemoveAll(rule => rule == temporaryBlock);
});
```

`GetFirewall()` is also available from `HttpSession` and `Controller`. All three accessors return the same `IFirewall` instance attached to the current server.

| Context | Accessor |
|---|---|
| Server | `server.GetFirewall()` |
| Request handler | `session.GetFirewall()` |
| Controller method | `this.GetFirewall()` |

Each call to `Update(...)` starts from a copy of the current global options. Properties that are not changed by the callback keep their existing values. The replacement is validated and then applied atomically.

- Concurrent updates are serialized, so successful changes are not lost.
- If the callback throws, the active configuration is unchanged.
- If validation fails, the active configuration is unchanged.
- Requests already in progress continue with the configuration they captured; subsequent requests use the replacement.
- Controller and method attributes are static and are not modified by `Update(...)`.

All global options can be changed through the same API, not only allow and deny rules:

```csharp
firewall.Update(options => {
    options.GlobalRateLimit = new RateLimitOptions {
        Limit = 200,
        Window = TimeSpan.FromMinutes(1),
        SlidingWindow = true
    };
    options.CountryCacheTtl = TimeSpan.FromMinutes(30);
});
```

Changing only allow, deny, or country rules preserves rate-limit counters and the GeoIP cache. Changing the global rate-limit policy resets its counters. Changing the GeoIP database path or effective cache TTL clears the country cache and reloads the reader safely.

Calling `GetFirewall()` before installing the module throws `InvalidOperationException`. Calling `UseFirewallModule(...)` a second time on the same server also throws `InvalidOperationException`; use `GetFirewall().Update(...)` for every runtime change.


## Handler Attributes

Handler-specific rules use attributes implementing SimpleW `IHandlerMetadata`.

| Attribute | Description |
|---|---|
| `[FirewallAllowIp("192.168.1.0/24", "127.0.0.1")]` | Allows matching IP/CIDR rules. If present, non-matching requests are denied. |
| `[FirewallDenyIp("203.0.113.10")]` | Denies matching IP/CIDR rules. |
| `[FirewallAllowCountry("FR", "BE")]` | Allows matching resolved countries. If present, non-matching requests are denied. |
| `[FirewallDenyCountry("RU", "CN")]` | Denies matching resolved countries. |
| `[FirewallAllowUnknownCountry]` | Allows requests whose country cannot be resolved. |
| `[FirewallDenyUnknownCountry]` | Denies requests whose country cannot be resolved. |
| `[FirewallRateLimit(5, 30, FirewallRateLimitWindowUnit.Seconds, slidingWindow: true)]` | Declares a handler rate limit. |

Attributes can be used on controller classes, controller methods, and non-inline delegate methods.
Inline lambdas cannot be decorated with C# attributes.


## Rule Resolution

Global rules apply when a handler has no firewall metadata.

When a handler has at least one firewall attribute:

- handler allow/deny/country rules replace global allow/deny/country rules
- handler rate limit replaces `GlobalRateLimit`
- if no handler rate limit is declared, `GlobalRateLimit` is still used

This mirrors the old path-override model: repeat global allow rules on the handler when the handler should keep the same allow list.

Decision order:

1. missing client IP returns `403`
2. deny IP rules return `403`
3. deny country rules return `403`
4. allow rules miss returns `403`
5. rate-limit whitelist skips rate limiting for matching IP/CIDR rules
6. rate limit returns `429`
7. otherwise the request continues


## IP Rules

```csharp
server.UseFirewallModule(options => {
    options.DenyRules.Add(IpRule.Single("203.0.113.42"));
    options.AllowRules.Add(IpRule.Cidr("192.168.1.0/24"));
});
```

Supported formats:

- single IPv4 or IPv6 address
- IPv4 or IPv6 CIDR notation

Attributes also accept either format:

```csharp
[FirewallAllowIp("10.0.0.0/8", "127.0.0.1")]
```


## Country Rules

Country filtering requires a MaxMind country database to resolve real countries.
If no database is configured, the country is unresolved.

```csharp
server.UseFirewallModule(options => {
    options.MaxMindCountryDbPath = "/app/data/GeoLite2-Country.mmdb";
    options.DenyCountries.Add(CountryRule.Any("RU", "CN"));
});
```

Handler-level country rules:

```csharp
[FirewallAllowCountry("FR", "BE", "CH")]
[Route("GET", "/billing")]
public object Billing()
{
    return new { ok = true };
}
```

Unknown country behavior:

```csharp
options.TreatUnknownCountryAsMatchable = true; // default
```

When enabled, unknown countries can match:

```csharp
options.DenyCountries.Add(CountryRule.Unknown());
```

or:

```csharp
[FirewallDenyUnknownCountry]
```

Country lookups are cached per IP. Set `CountryCacheTtl` to tune cache duration.


## Rate Limiting

Global rate limit:

```csharp
server.UseFirewallModule(options => {
    options.GlobalRateLimit = new RateLimitOptions {
        Limit = 100,
        Window = TimeSpan.FromSeconds(10),
        SlidingWindow = false
    };

    // Trusted internal clients bypass both global and handler rate limits.
    options.RateLimitWhitelistRules.Add(IpRule.Single("127.0.0.1"));
    options.RateLimitWhitelistRules.Add(IpRule.Cidr("10.0.0.0/8"));
});
```

Handler rate limit:

```csharp
[FirewallRateLimit(5, 30, FirewallRateLimitWindowUnit.Seconds, slidingWindow: true)]
[Route("POST", "/login")]
public object Login()
{
    return new { ok = true };
}
```

Fixed window is faster and uses less memory. Sliding window is more precise and better for sensitive endpoints.
Rate-limit state is keyed by client IP. `RateLimitWhitelistRules` only skips rate limiting; matching clients still go through deny rules, country rules, and allow-list checks.


## Client IP Resolution

The firewall uses:

```csharp
session.ClientIpAddress
```

For reverse proxies, configure SimpleW explicitly:

```csharp
server.ConfigureClientIPResolver(session => {
    if (session.Request.Headers.TryGetValue("X-Real-IP", out string? value)
        && IPAddress.TryParse(value, out IPAddress? forwardedIp)) {
        return forwardedIp;
    }

    if (session.RemoteEndPoint is not IPEndPoint endpoint) {
        return null;
    }

    return endpoint.Address;
});
```

Only trust forwarded IP headers from trusted proxy infrastructure.


## Static Files And Fallbacks

`PathRule` / `PathRules` has been removed.

Static files and fallback routes usually do not carry custom attributes directly, so protect them with global firewall rules. If a fallback or static-like handler is mapped through a decorated method, handler metadata works normally.


## Telemetry

Enable telemetry:

```csharp
server.UseFirewallModule(options => {
    options.EnableTelemetry = true;
});
```

The underlying `SimpleWServer` telemetry must also be enabled.

Metrics:

- `simplew.firewall.decision.count`
- `simplew.firewall.block.count`
- `simplew.firewall.ratelimit.count`
- `simplew.firewall.match.allow.count`
- `simplew.firewall.match.deny.count`
- `simplew.firewall.match.allow_miss.count`
- `simplew.firewall.decision.duration`
- `simplew.firewall.tracked_ips.fixed`
- `simplew.firewall.tracked_ips.sliding`
- `simplew.firewall.tracked_ips.total`
- `simplew.firewall.tracked_ips.country_cache`
- `simplew.firewall.rules.allow.global`
- `simplew.firewall.rules.deny.global`
- `simplew.firewall.rules.allow_countries.global`
- `simplew.firewall.rules.deny_countries.global`

Decision tags use low-cardinality values such as `result`, `reason`, `scope`, and `window`.


## HTTP Responses

- `403 Forbidden` for missing IP, deny matches, and allow misses
- `429 Too Many Requests` for rate limiting

Requests are rejected before controller execution.
