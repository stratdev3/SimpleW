# Latency

The [`SimpleW.Service.Latency`](https://www.nuget.org/packages/SimpleW.Service.Latency) package provides a **latency injection module** for the SimpleW web server.

It allows you to **artificially delay HTTP responses** in a controlled and deterministic way, without modifying your application code.

This module is designed to :
- simulate slow downstream services
- test client-side timeouts and retry logic
- reproduce real-world latency scenarios
- validate application behavior under degraded conditions


## Requirements

- .NET 8.0+
- SimpleW (core server)

No external dependencies.


## Installation

```sh
$ dotnet add package SimpleW.Service.Latency --version 26.0.0-beta.20260208-1374
```


## Basic Usage

### Minimal Example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Latency;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseLatencyModule(options => {
    options.GlobalLatency = TimeSpan.FromMilliseconds(200);
});

await server.RunAsync();
```

In this configuration :
- **all HTTP responses** are delayed by 200 ms
- latency is applied **before** the request handler executes


## Path-based latency rules

Latency can be applied selectively based on request paths.

### Eact path rules

```csharp
server.UseLatencyModule(options => {

    options.Rules.Add(new LatencyRule(
        "/api/health",
        TimeSpan.FromSeconds(2)
    ));

    options.Rules.Add(new LatencyRule(
        "/login",
        TimeSpan.FromMilliseconds(500)
    ));
});
```

These rules :
- apply **only** to exact path matches
- are resolved using an O(1) lookup
- take priority over wildcard rules


## Prefix / wildcard rules

Latency can also be applied to **path prefixes**.

```csharp
server.UseLatencyModule(options => {

    // implicit wildcard (equivalent to "/api/*")
    options.Rules.Add(new LatencyRule(
        "/api/",
        TimeSpan.FromMilliseconds(300)
    ));

    // explicit wildcard
    options.Rules.Add(new LatencyRule(
        "/assets/*",
        TimeSpan.FromSeconds(1)
    ));
});
```

Matching behavior :
- rules ending with `/` or `/*` are treated as **prefix rules**
- matching uses `StartsWith`
- the **most specific prefix wins**


## Rule priority and resolution

Latency resolution follows this order :
1. **Exact path rules**
2. **Prefix / wildcard rules**, sorted by prefix length (most specific first)
3. Optional `*` catch-all rule
4. Global latency (fallback)

Example :

```csharp
server.UseLatencyModule(options => {

    options.Rules.Add(new LatencyRule(
        "/api/v1/users",
        TimeSpan.FromSeconds(2)
    ));

    options.Rules.Add(new LatencyRule(
        "/api/",
        TimeSpan.FromMilliseconds(200)
    ));

    options.GlobalLatency = TimeSpan.FromMilliseconds(50);
});
```

Result :
- `/api/v1/users` → 2 s
- `/api/v1/posts` → 200 ms
- `/health` → 50 ms


## Catch-all rule

You can define a rule that matches **all paths** using `*`.

```csharp
server.UseLatencyModule(options => {

    options.Rules.Add(new LatencyRule(
        "*",
        TimeSpan.FromMilliseconds(150)
    ));

    options.GlobalLatency = TimeSpan.FromMilliseconds(20);
});
```

The `*` rule :
- is evaluated **before** `GlobalLatency`
- allows global behavior with higher priority


## Enabling and disabling latency dynamically

The module can be enabled or disabled at runtime.

```csharp
LatencyOptions latencyOptions = new();

server.UseLatencyModule(o => {
    latencyOptions = o;
    o.GlobalLatency = TimeSpan.FromMilliseconds(300);
});

// later...
latencyOptions.Enabled = false;
```

When disabled :
- no latency is applied
- the middleware becomes a no-op


## Important behavior notes

- Latency is applied before controller execution
- Latency affects the entire request, including routing and handlers
- A latency value of TimeSpan.Zero has no effect and is ignored
- No background timers or threads are used
- No allocations occur on the fast path (except Task.Delay)
