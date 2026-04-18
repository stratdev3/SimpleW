# Dependency Injection

The [`SimpleW.Helper.DependencyInjection`](https://www.nuget.org/packages/SimpleW.Helper.DependencyInjection) package adds dependency injection support for SimpleW controllers.

It is designed for applications that want to keep the regular `MapController()` / `MapControllers()` API while resolving controllers through a `Microsoft.Extensions.DependencyInjection` container.

## Features

It allows you to:
- create one `IServiceScope` per request
- use constructor injection in controllers
- keep the standard `MapController()` / `MapControllers()` flow
- resolve request services from `HttpSession` or `Controller`
- integrate cleanly with `SimpleW.Helper.Hosting`

It does **not** replace SimpleW routing.
Route discovery, `[Route]` attributes, and route metadata stay in the SimpleW core pipeline.


## Requirements

- .NET 8.0
- SimpleW (core server)
- `Microsoft.Extensions.DependencyInjection`


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.DependencyInjection --version 26.0.0-rc.20260418-1789
```


## What it changes

Once `UseDependencyInjection()` is enabled, controllers are activated through the configured `IServiceProvider`.

That means this works:

```csharp
[Route("/users")]
public sealed class UsersController : Controller {

    private readonly UserService _users;

    public UsersController(UserService users) {
        _users = users;
    }

    [Route("GET", "/:id")]
    public object Get(string id) {
        return _users.Get(id);
    }

}
```

Here:
- `UserService` comes from DI
- `id` comes from the HTTP route


## Minimal example

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SimpleW;
using SimpleW.Observability;
using SimpleW.Helper.DependencyInjection;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // DI
            ServiceCollection services = new();
            services.AddScoped<GreetingService>();
            await using ServiceProvider provider = services.BuildServiceProvider();

            // setup simplew
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.UseDependencyInjection(provider);
            server.MapController<HomeController>("/api");

            // run server
            await server.RunAsync();
        }
    }

    [Route("/home")]
    public class HomeController : Controller {

        private readonly GreetingService _greetings;

        public HomeController(GreetingService greetings) {
            _greetings = greetings;
        }

        [Route("GET", "/hello/:name")]
        public object Hello(string name, string prefix = "Hello") {
            return new {
                message = $"{prefix} {name}{_greetings.Suffix}"
            };
        }

    }

    public class GreetingService {
        public string Suffix => " from DI";
    }

}
```


## Request services

The package also exposes helpers to access the current request service provider.

These helpers are extension methods from `SimpleW.Helper.DependencyInjection`.
They are not built-in members of `HttpSession` or `Controller`.

Make sure the file imports:

```csharp
using SimpleW.Helper.DependencyInjection;
```

### On `HttpSession`

| Method | Description |
| ------ | ----------- |
| `GetRequestServices()` | Returns the current request `IServiceProvider`. Throws when DI is not enabled. |
| `TryGetRequestServices(out services)` | Returns `true` when request services are available. |
| `GetRequestService<T>()` | Resolves a service or returns `null`. |
| `GetRequiredRequestService<T>()` | Resolves a required service or throws. |

The same helpers are available as extension methods on `Controller`.


Example:

:::: code-group

```csharp [From Controller]
using SimpleW.Helper.DependencyInjection;

[Route("/debug")]
public sealed class DebugController : Controller {

    [Route("GET", "/service")]
    public object Service() {
        DebugMarker marker = this.GetRequiredRequestService<DebugMarker>();
        return new { id = marker.Id };
    }

}
```

```csharp [From Session]
server.MapGet("/service", (HttpSession session) => {
    DebugMarker marker = session.GetRequiredRequestService<DebugMarker>();
    return new { id = marker.Id };
});
```

::::


## Generic Host integration

When using [`SimpleW.Helper.Hosting`](./helper-hosting.md), the recommended pattern is:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SimpleW.Helper.DependencyInjection;
using SimpleW.Helper.Hosting;

var builder = SimpleWHost.CreateApplicationBuilder(args);

builder.Services.AddScoped<MyService>();

builder.ConfigureSimpleW((services, server) => {
    server.UseDependencyInjection(services);
    server.MapController<HomeController>("/api");
});

var host = builder.Build();
await host.RunAsync();
```

This ensures controller DI is enabled before `MapController()` runs.


## Advanced usage

If the root `IServiceProvider` is not available yet when configuring the server, you can defer it:

```csharp
server.UseDependencyInjection(() => rootProvider);
```

This overload is useful when the service provider is created later in the application lifecycle.

For already built hosts, `UseSimpleWDependencyInjection()` still exists as a convenience helper:

```csharp
var host = builder.Build()
                  .UseSimpleWDependencyInjection();
```

That helper is mainly useful in manual or advanced hosting scenarios where the `SimpleWServer` was already configured elsewhere.


## Scope behavior

- A fresh `IServiceScope` is created for each request.
- Scoped services are isolated between requests.
- Singleton services are reused normally by the root container.
- Controllers are created per request.
- Controller disposal is handled after the action completes.


## Notes

- Call `UseDependencyInjection()` before `MapController()` / `MapControllers()`.
- Controller constructor parameters can come from DI.
- Action method parameters do **not** come from DI. They still come from route/query binding.
- If you need custom authorization or policy behavior, keep using [middleware](../guide/middleware.md) plus route [metadata attributes](../guide/handler-attribute.md).
