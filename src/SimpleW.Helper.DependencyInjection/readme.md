# SimpleW.Helper.DependencyInjection

Dependency injection helpers for SimpleW.

Namespace: `SimpleW.Helper.DependencyInjection`

## Features

- Per-request `IServiceScope`
- DI constructor activation for controllers
- Standard `MapController()` / `MapControllers()` support once DI is enabled
- `GetRequestServices()` helpers on `HttpSession` and `Controller`

## Getting Started

```cs
using Microsoft.Extensions.DependencyInjection;
using SimpleW;
using SimpleW.Helper.DependencyInjection;

ServiceCollection services = new();
services.AddScoped<MyService>();

ServiceProvider provider = services.BuildServiceProvider();

SimpleWServer server = new(System.Net.IPAddress.Loopback, 8080);
server.UseDependencyInjection(provider);
server.MapController<HomeController>("/api");
```

```cs
[Route("/home")]
public sealed class HomeController : Controller {

    private readonly MyService _service;

    public HomeController(MyService service) {
        _service = service;
    }

    [Route("GET", "/hello")]
    public object Hello() {
        return new { message = _service.Message };
    }

}
```

## Notes

- Controllers are created per request.
- Action parameters keep the existing SimpleW route/query binding behavior.
- Call `UseDependencyInjection()` before `MapController()` / `MapControllers()` so controller actions are activated through DI.
- Routing, `[Route]` discovery, and route metadata stay in the SimpleW core pipeline. This package only swaps the controller action activation path.
- `GetRequestServices()`, `GetRequestService<T>()`, and related helpers are extension methods from `SimpleW.Helper.DependencyInjection`, so the file must import that namespace.

## Generic Host

When using `SimpleW.Helper.Hosting`, prefer the `ConfigureSimpleW` overload that receives the built `IServiceProvider`:

```cs
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

This keeps the DI action executor factory and the request scope setup in the same place, before `MapController(...)` runs.

`UseSimpleWDependencyInjection()` is still available for advanced/manual hosting scenarios where the `SimpleWServer` has already been configured elsewhere.
