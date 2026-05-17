# SimpleW.Service.Firewall

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)

## Features

This package provides an application-level firewall for SimpleW:

- global allow and deny rules by IP/CIDR
- global allow and deny rules by country with MaxMind GeoIP2
- handler-level firewall rules using SimpleW handler metadata attributes
- fixed-window and sliding-window rate limiting
- bounded per-IP state with TTL cleanup
- optional telemetry counters and gauges

## Getting Started

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Firewall;

var server = new SimpleWServer(IPAddress.Any, 2015);

server.UseFirewallModule(fw => {
    fw.AllowRules.Add(IpRule.Cidr("10.0.0.0/8"));
    fw.AllowRules.Add(IpRule.Single("127.0.0.1"));

    fw.GlobalRateLimit = new RateLimitOptions {
        Limit = 200,
        Window = TimeSpan.FromSeconds(10)
    };
});

server.MapController<AdminController>("/api");

await server.RunAsync();

[Route("/admin")]
public sealed class AdminController : Controller
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

Global rules apply to handlers that do not declare firewall metadata.
As soon as a handler has at least one firewall attribute, its handler policy replaces global allow/deny/country rules. Handler rate limits override the global rate limit; if no handler rate limit is declared, the global rate limit still applies.

## Handler Attributes

```csharp
[FirewallAllowIp("192.168.1.0/24", "127.0.0.1")]
[FirewallDenyIp("203.0.113.10")]
[FirewallAllowCountry("FR", "BE", "CH")]
[FirewallDenyCountry("RU", "CN")]
[FirewallAllowUnknownCountry]
[FirewallDenyUnknownCountry]
[FirewallRateLimit(5, 30, FirewallRateLimitWindowUnit.Seconds, slidingWindow: true)]
```

Attributes work on controller classes, controller methods, and non-inline delegate methods.
Class metadata is evaluated before method metadata; method rate-limit metadata wins when both class and method declare one.

## Notes

- `PathRule` / `PathRules` has been removed in favor of handler metadata.
- Static files, fallback routes, and anonymous inline lambdas cannot carry attributes directly. Use global rules for those, or route through decorated handlers.
- The firewall uses `session.ClientIpAddress`, so reverse proxy setups should configure `server.ConfigureClientIPResolver(...)`.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/Stratdev3/SimpleW/blob/master/release.md).

## License

This library is under the MIT License.
