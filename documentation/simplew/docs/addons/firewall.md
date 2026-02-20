# Firewall

[`SimpleW.Service.Firewall`](https://www.nuget.org/packages/SimpleW.Service.Firewall) is an optional **application-level firewall module** for the SimpleW web server.
It provides **fast IP filtering and rate limiting**, implemented as a SimpleW middleware.


## Features

It allows you to :
- Allow or deny requests based on **client IP**
- Allow or deny requests based on **client country** (GeoIP2 / MaxMind, optional)
- Define rules globally or **per-path**
- Use **CIDR notation** (IPv4 and IPv6)
- Apply **rate limiting** :
  - fixed window
  - sliding window
- Automatically clean internal state using **TTL + hard caps**
- Protect sensitive endpoints with minimal overhead


## Requirements

- .NET 8.0
- SimpleW (core server)
- MaxMind.GeoIP2 (automatically included)

Optional dependency if you enable GeoIP country filtering :
- a MaxMind `.mmdb` database (ex: GeoLite2-Country.mmdb)


## Installation

```sh
$ dotnet add package SimpleW.Service.Firewall --version 26.0.0-beta.20260221-1486
```


## Configuration options

| Option | Default | Description |
|---|---|---|
| ClientIpResolver | `session => session.Socket.RemoteEndPoint as IPEndPoint ? ep.Address : null` | Resolves the client IP address from the `HttpSession` (used for IP rules + rate limiting keys). |
| PathRules | `[]` | Path-based overrides (**first match wins**). Each rule targets a URL prefix and can define its own allow/deny/country/rate-limit rules. |
| AllowRules | `[]` | Global allow list by IP/CIDR. **If not empty:** everything not matching an allow rule is **denied by default**. |
| DenyRules | `[]` | Global deny list by IP/CIDR (checked against the resolved client IP). |
| GlobalRateLimit | `null` | Global rate limit policy. `null` disables global rate limiting. |
| StateTtl | `10 minutes` | Retention for inactive IP state (used for internal tracking / cleanup). Must be `> 0`. |
| MaxTrackedIps | `50000` | Safety cap on how many IPs can be tracked internally. Must be `> 0`. |
| CleanupEveryNRequests | `10000` | Opportunistic cleanup frequency (every N requests). Must be `> 0`. |
| EnableTelemetry | `false` | Enables module telemetry (note: the underlying `SimpleWServer.Telemetry` must also be enabled). |
| MaxMindCountryDbPath | `null` | Optional MaxMind GeoIP2 country database path (`.mmdb`). If `null`/empty, country rules can’t be evaluated (treated as unknown). |
| TreatUnknownCountryAsMatchable | `true` | If `true`, unresolved country can match `CountryRule.Unknown()`. If `false`, unknown country never matches country rules. |
| CountryCacheTtl | `null` | Cache duration for IP -> country resolution. If `null`, the module uses `StateTtl`. Must be `> 0` when set. |
| AllowCountries | `[]` | Global allow list by country (ISO2 like `"FR"`, `"US"`). **If not empty:** default deny for non-matching countries (same behavior as `AllowRules`). |
| DenyCountries | `[]` | Global deny list by country (ISO2 like `"FR"`, `"US"`). |
| PathRule.Prefix | `"/"` | Prefix to match (normalized prefix). Used to apply per-path overrides. |
| PathRule.Allow | `[]` | Allow list by IP/CIDR for this path. **If not empty:** default deny for this path if no allow match. |
| PathRule.Deny | `[]` | Deny list by IP/CIDR for this path. |
| PathRule.AllowCountries | `[]` | Allow list by country for this path. **If not empty:** default deny for this path if no allow match. |
| PathRule.DenyCountries | `[]` | Deny list by country for this path. |
| PathRule.RateLimit | `null` | Rate limit policy for this path. `null` disables per-path rate limiting. |
| RateLimitOptions.Limit | `100` | Max number of requests allowed within the window. Must be `> 0` (practically). |
| RateLimitOptions.Window | `10 seconds` | Time window for rate limiting. Must be `> 0`. |
| RateLimitOptions.SlidingWindow | `false` | `false` = fixed window counter. `true` = sliding-ish window (stores timestamps). |


## Minimal Example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Firewall;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseFirewallModule(options => {
    // allow only private networks (default deny)
    options.AllowRules.Add(IpRule.Cidr("192.168.0.0/16"));
    options.AllowRules.Add(IpRule.Cidr("10.0.0.0/8"));
});

await server.RunAsync();
```

In this configuration :
- Any IP **not matching the allowlist** is rejected
- Requests are blocked **before routing**


## IP allow / deny rules

### Global rules

```csharp
server.UseFirewallModule(options => {
    options.DenyRules.Add(IpRule.Single("203.0.113.42"));
    options.AllowRules.Add(IpRule.Cidr("192.168.1.0/24"));
});
```

Rules support :
- single IPs
- CIDR notation
- IPv4 and IPv6

If `AllowRules` is not empty, the firewall operates in **default deny** mode.


### Per-path rules

Rules can be applied to specific URL prefixes.

```csharp
server.UseFirewallModule(options => {

    options.PathRules.Add(new PathRule {
        Prefix = "/admin",
        Allow = {
            IpRule.Cidr("192.168.1.0/24")
        }
    });

});
```

### Rule resolution

- Path rules are matched using `StartsWith`
- The **most specific prefix wins**
- Prefixes are automatically sorted by length at startup

Example :
- `/api/admin` is evaluated before `/api`
- `/api` is evaluated before `/`


## GeoIP country filtering (MaxMind)

The firewall can optionally filter requests based on the **client country**, resolved from the client IP using a **MaxMind GeoIP2 database**.

This feature is **disabled by default**.  
When enabled, you can define **allow/deny rules by country** globally and/or per-path, exactly like IP allow/deny rules.

### Enabling GeoIP

```csharp
server.UseFirewallModule(options => {
    options.MaxMindCountryDbPath = "/app/data/GeoLite2-Country.mmdb"; // adjust path depending on your mmdb location
});
```

### Country rules (global)

Country codes use ISO2 format (examples: `"FR"`, `"US"`, `"DE"`).

```csharp
server.UseFirewallModule(options => {

    options.MaxMindCountryDbPath = "/app/data/GeoLite2-Country.mmdb"; // adjust path depending on your mmdb location

    // Deny RU and CN globally
    options.DenyCountries.Add(CountryRule.Any("RU", "CN"));

    // If AllowCountries is not empty => default deny for all other countries
    options.AllowCountries.Add(CountryRule.Any("FR", "BE", "CH"));
});
```

Behavior :
- If `AllowCountries` is not empty, the firewall operates in **default deny** mode for countries (same behavior as AllowRules for IPs).
- `DenyCountries` is always evaluated first.

### Country rules (per-path)

You can define country allow/deny rules for specific prefixes :

```csharp
server.UseFirewallModule(options => {

    options.MaxMindCountryDbPath = "/app/data/GeoLite2-Country.mmdb"; // adjust path depending on your mmdb location

    options.PathRules.Add(new PathRule {
        Prefix = "/admin",

        // Only allow admin access from FR/BE/CH
        AllowCountries = { CountryRule.Any("FR", "BE", "CH") }
    });

});
```

Per-path behavior :
- Path rules are evaluated first (most specific prefix wins).
- If a path rule has `AllowCountries`, it becomes **default deny** for that path unless a country matches.

### Unknown / unresolved country

Sometimes a country cannot be resolved :
- database not configured
- IP not in the database
- lookup error

In that case, the resolved country is `unknown`.

You can choose how the firewall treats unknown countries:

```csharp
options.TreatUnknownCountryAsMatchable = true; // default
```

If `TreatUnknownCountryAsMatchable` is true, you can explicitly match unknown countries:

```csharp
options.DenyCountries.Add(CountryRule.Unknown());
```

If `TreatUnknownCountryAsMatchable` is false, unknown countries will **never match** any country rule.

### Caching and performance

Country resolution is cached :
- IP → Country ISO2 is cached in-memory
- TTL is controlled by:
  - `CountryCacheTtl` (if set)
  - otherwise `StateTtl`

```csharp
options.CountryCacheTtl = TimeSpan.FromMinutes(10);
```

The cache is bounded by the same memory safety mechanisms :
- TTL eviction
- hard cap (`MaxTrackedIps`)
- opportunistic cleanup (`CleanupEveryNRequests`)

This ensures GeoIP support remains fast and safe under load.


## Rate limiting

### Global rate limiting

```csharp
server.UseFirewallModule(options => {

    options.GlobalRateLimit = new RateLimitOptions {
        Limit = 100,
        Window = TimeSpan.FromSeconds(10)
    };

});
```

This limits each client IP to **100 requests per 10 seconds**.


### Per-path rate limiting

```csharp
server.UseFirewallModule(options => {

    options.PathRules.Add(new PathRule {
        Prefix = "/login",
        RateLimit = new RateLimitOptions {
            Limit = 5,
            Window = TimeSpan.FromSeconds(30),
            SlidingWindow = true
        }
    });

});
```

This configuration :
- applies only to `/login`
- prevents burst abuse
- is independent from the global rate limit


## Fixed window vs Sliding window

### Fixed window (default)

- Very fast
- Minimal memory usage
- Allows small bursts at window boundaries

Best suited for :
- internal APIs
- LAN traffic
- general protection

```csharp
SlidingWindow = false
```


### Sliding window

- More precise
- Prevents burst exploits
- Slightly higher memory and CPU cost

Best suited for :
- authentication endpoints
- public APIs
- sensitive routes

```csharp
SlidingWindow = true
```


## Memory safety and cleanup

To prevent unbounded memory growth, the firewall automatically manages its internal state.

### Built-in protections

- Per-IP state uses a **time-to-live (TTL)**
- Hard limit on the number of tracked IPs
- Opportunistic cleanup during request handling
- No background threads or timers

### Default values

```csharp
options.StateTtl = TimeSpan.FromMinutes(10);
options.MaxTrackedIps = 50_000;
options.CleanupEveryNRequests = 10_000;
```

These defaults are safe for most deployments and can be tuned if needed.


## Client IP resolution

By default, the firewall uses the TCP remote endpoint :

```csharp
session.Socket.RemoteEndPoint
```

You can override this behavior if needed :

```csharp
options.ClientIpResolver = (HttpSession session) => {

    // 1. look for any X-Real-IP header (note: you should check this value come from a trust proxy)
    if (session.Request.Headers.TryGetValue("X-Real-IP", out string? XRealIp)) {
        return IPEndPoint.Parse(XRealIp).Address;
    }

    // 2. client ip (fallback)
    if (session.Socket.RemoteEndPoint is not IPEndPoint ep) {
        return null;
    }
    return ep.Address;
};
```

This is useful when integrating with reverse proxies or custom transports.


## HTTP responses

When a request is blocked :
- **403 Forbidden** is returned for allow/deny rules
- **429 Too Many Requests** is returned for rate limiting

Requests are rejected **before** routing and controller execution.


## Telemetry & Counters

The firewall module can optionally emit **telemetry and metrics** to help you observe its behavior in production.

When enabled, the module exposes :
- structured **Activity / tracing spans**
- **counters and histograms** for allowed, denied, and rate-limited requests
- minimal overhead when disabled

Telemetry is fully optional and disabled by default.

### Enabling telemetry

```csharp
server.UseFirewallModule(options => {
    options.EnableTelemetry = true;
});
```

### What is tracked

When `EnableTelemetry` is set to `true`, the firewall reports :

**Tracing (Activity)**

Each firewall decision may create an Activity containing tags such as :
- `firewall.action` → allow | deny | rate_limit
- `client.ip` → resolved client IP
- `path.prefix` → matched path rule (if any)
- `rate_limit.limit` → configured limit
- `rate_limit.window_ms` → window size

This allows correlation with the rest of the SimpleW request pipeline.

**Metrics**

Typical counters and instruments include :
- `firewall.requests.allowed`
- `firewall.requests.denied`
- `firewall.requests.rate_limited`
- `firewall.state.tracked_ips`
- `firewall.cleanup.runs`

These metrics can be exported through the same observability pipeline used by SimpleW.

### Performance impact

- When disabled, telemetry adds **near-zero overhead**
- When enabled, all instruments are designed to be allocation-light
- No background threads are introduced

### When to enable

Recommended for :
- production environments
- tuning rate limits
- diagnosing unexpected blocks
- validating new firewall rules

For extremely latency-sensitive deployments, telemetry can remain disabled.
