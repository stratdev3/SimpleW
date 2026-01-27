# Firewall

[`SimpleW.Service.Firewall`](https://www.nuget.org/packages/SimpleW.Service.Firewall) is an optional **application-level firewall module** for the SimpleW web server.
It provides **fast IP filtering and rate limiting**, implemented as a SimpleW middleware.


## What this package does

`SimpleW.Modules.Firewall` allows you to :
- Allow or deny requests based on **client IP**
- Define rules globally or **per-path**
- Use **CIDR notation** (IPv4 and IPv6)
- Apply **rate limiting** :
  - fixed window
  - sliding window
- Automatically clean internal state using **TTL + hard caps**
- Protect sensitive endpoints with minimal overhead

It **does not** :
- inspect headers or request bodies
- try to detect attacks heuristically
- replace a real network firewall or reverse proxy
- add any dependency outside SimpleW


## Requirements

- .NET 8.0+
- SimpleW (core server)

No external dependencies.


## Installation

```sh
$ dotnet add package SimpleW.Modules.Firewall --version 26.0.0-beta.20260129-0011
```


## Basic Usage

### Minimal Example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Modules;

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


## What this module is NOT

This module is intentionally limited.

It does **not** :
- inspect headers
- parse request bodies
- detect SQL injection or XSS
- replace a WAF or CDN
- implement authentication or authorization

If you need those features, use a reverse proxy or a dedicated security layer.