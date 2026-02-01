# Chaos

The [`SimpleW.Service.Chaos`](https://www.nuget.org/packages/SimpleW.Service.Chaos) package provides a [**chaos engineering module**](https://en.wikipedia.org/wiki/Chaos_engineering) for the SimpleW web server.

It allows you to **intentionally inject failures** into your API :
- HTTP errors (`403`, `404`, `500`...)
- artificial latency
- **connection drops** (graceful close or TCP RST)

This module is designed to test **client resilience, retry logic, timeouts**, and **observability**, without modifying your application code.


## Requirements

- .NET 8.0+
- SimpleW (core server)

No external dependencies.


## Installation

```sh
$ dotnet add package SimpleW.Service.Chaos --version 26.0.0-beta.20260202-1339
```


## Basic Usage

### Minimal Example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Chaos;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseChaosModule(options => {
    options.Enabled = true;
    options.Prefix = "/api";
    options.Probability = 0.10; // 10% of requests fail
});

await server.RunAsync();
```

In this configuration :
- Chaos applies only to `/api`
- 10% of matching requests will be sabotaged
- All failures are HTTP `500` by default


## Path and method filtering

Restrict chaos to a path prefix

```csharp
options.Prefix = "/api";
```

Matching uses StartsWith :
- `/api/users` → affected
- `/health` → not affected


## Restrict chaos to HTTP methods

```csharp
options.Methods = new HashSet<string> {
    "GET",
    "POST"
};
```

If Methods is null or empty, all methods are affected.


## HTTP error injection

Fixed status code

```csharp
options.FixedStatusCode = 503;
```

Every sabotaged request returns the same HTTP status.


## Weighted random status codes

```csharp
options.StatusWeights = new Dictionary<int, double> {
    [403] = 1,
    [404] = 2,
    [500] = 5
};
```

This configuration :
- returns `500` most of the time
- occasionally returns `404`
- rarely returns `403`

Weights are relative, not percentages.


## Response body

By default, Chaos returns a simple text body :

```html
CHAOS: simulated error 500 Internal Server Error
```

You can customize or disable it :

```csharp
options.BodyTemplate = "CHAOS {code} {text}";
```

Available placeholders :
- `{code}` → HTTP status code
- `{text}` → HTTP status text

To disable the body entirely :

```csharp
options.BodyTemplate = null;
```


## Artificial latency

You can inject latency into sabotaged requests :

```csharp
options.MinDelayMs = 50;
options.MaxDelayMs = 250;
```

Latency is applied **only when chaos triggers**, not on normal requests.

Use cases :
- simulate slow downstream services
- test client timeouts
- reveal hidden synchronous bottlenecks


## Connection failures (network chaos)

Chaos can simulate **network-level failures** by closing the TCP connection without a response.

```csharp
options.CloseConnectionProbability = 0.20;
```

This means :
- 20% of sabotaged requests will result in **no HTTP response**


### TCP RST vs graceful close

```csharp
options.AbortWithRst = true;
```

- `true` → force **TCP RST** (hard failure)
- `false` → graceful socket close

RST is useful to test :
- retry logic
- connection reuse issues
- aggressive client behavior



