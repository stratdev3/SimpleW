# Chaos

The [`SimpleW.Service.Chaos`](https://www.nuget.org/packages/SimpleW.Service.Chaos) package provides a [**chaos engineering module**](https://en.wikipedia.org/wiki/Chaos_engineering) for the SimpleW web server.


## Features

It allows you to **intentionally inject failures** into your API :
- HTTP errors (`403`, `404`, `500`...)
- artificial latency
- **connection drops** (graceful close or TCP RST)

This module is designed to test **client resilience, retry logic, timeouts**, and **observability**, without modifying your application code.


## Requirements

- .NET 8.0
- SimpleW (core server)

No external dependencies.


## Installation

```sh
$ dotnet add package SimpleW.Service.Chaos --version 26.0.0-beta.20260202-1339
```


## Configuration options

| Option name | Default value | Description |
|------------|---------------|-------------|
| Enabled | `false` | Enables or disables the chaos module globally. When disabled, requests are never sabotaged. |
| Prefix | `"/api"` | Applies chaos only to requests whose path starts with this prefix. The prefix is normalized internally. |
| Methods | `null` | Limits chaos to specific HTTP methods (e.g. `GET`, `POST`). If null or empty, all methods are allowed. |
| Probability | `0.0` | Probability (between 0 and 1) that a matching request will be sabotaged. |
| CloseConnectionProbability | `0.0` | Among sabotaged requests, probability (between 0 and 1) to drop the TCP connection without sending a response. |
| AbortWithRst | `true` | If true, the connection is aborted using TCP RST (immediate reset). If false, the socket is closed gracefully. |
| MinDelayMs | `0` | Minimum artificial latency (in milliseconds) added before sabotaging a request. |
| MaxDelayMs | `0` | Maximum artificial latency (in milliseconds) added before sabotaging a request. If lower than `MinDelayMs`, it is adjusted automatically. |
| FixedStatusCode | `null` | Forces a single HTTP status code for all sabotaged responses (e.g. `500`). Must be a valid HTTP status code. |
| StatusWeights | `{ 500: 1 }` | Weighted HTTP status codes used when `FixedStatusCode` is not set. Keys are status codes, values are weights (>0). |
| Seed | `null` | Optional random seed for deterministic and reproducible chaos behavior. |
| BodyTemplate | `"CHAOS: simulated error {code} {text}"` | Response body template for HTTP errors. `{code}` and `{text}` placeholders are replaced. If null or empty, no body is sent. |


## Minimal Example

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



