---
title: Migration
description: Upgrade an existing SimpleW application or migrate an EmbedIO or ASP.NET Core application to SimpleW.
outline: deep
---

# Migration

This page helps you move to the current version of SimpleW, whether you are upgrading an older SimpleW application or replacing another .NET web server.

Choose the starting point that matches your application:

| Starting point | Recommended path |
| --- | --- |
| SimpleW v26.0 | Follow the [v26.0 to v26.1 guide](../guide/migrate-from-v26.0-to-v26.1.md). |
| SimpleW v16 | Follow the [v16 to v26.0 guide](../guide/migrate-from-v16-to-v26.md), then the v26.0 to v26.1 guide. |
| EmbedIO | Replace the server and modules first, then migrate controllers and request/response code. |
| ASP.NET Core Minimal APIs | Use delegate handlers for the closest SimpleW equivalent. |
| ASP.NET Core controllers | Use SimpleW controllers and make routing and body mapping explicit. |
| ASP.NET Core with Generic Host and DI | Keep the familiar host, configuration, logging and service container with the official addons. |

::: warning Not a drop-in replacement
SimpleW has its own HTTP pipeline, routing, request and response types. Packages that depend on ASP.NET Core middleware, MVC filters, model binding, authentication handlers or `HttpContext` cannot be reused unchanged. Keep the underlying application and domain services, but adapt the web-facing layer.
:::


## Plan the migration

For anything larger than a few routes, migrate in small checkpoints:

1. Keep the existing public HTTP contract: paths, methods, status codes, headers and JSON shapes.
2. Start a SimpleW server on a different local port and move one simple health or read-only route.
3. Move cross-cutting behavior such as logging, CORS and authentication into SimpleW middleware or modules.
4. Move endpoints by feature, validating the HTTP contract after each group.
5. Migrate static files, uploads, WebSockets, TLS and hosting only after ordinary HTTP routes work.
6. Switch production traffic only when the two implementations behave the same for your clients.

This approach keeps transport changes separate from business-logic changes and makes rollback straightforward.


## From SimpleW

Apply **the guides in chronological order**. **Do not skip an intermediate guide** when it matches your starting version, because a later guide assumes the earlier breaking changes have already been applied.

### From v26.0 to v26.1

The [v26.0 to v26.1 guide](../guide/migrate-from-v26.0-to-v26.1.md) covers the new network engine contract, server lifecycle state, TLS ownership, `HttpSession` transport changes and telemetry lifecycle.

Most applications using only routes, controllers, requests and responses need mechanical changes. Custom engines and code accessing sockets or streams directly require more work.

### From v16 to v26

The [v16 to v26.0 guide](../guide/migrate-from-v16-to-v26.md) covers the server, request, response, controller and extension API renames introduced by the v26 rewrite.

After completing it, continue with the [v26.0 to v26.1 guide](../guide/migrate-from-v26.0-to-v26.1.md).


## From ASP.NET Core

There are two useful migration styles:

- **Convert to the SimpleW style** for the smallest runtime and the most direct SimpleW API.
- **Keep the .NET Generic Host style** when the application already depends on `IServiceCollection`, scoped services, configuration, logging or hosted services.

Both styles use the SimpleW HTTP pipeline. The second style preserves the surrounding .NET application infrastructure; it does not make ASP.NET Core middleware or `HttpContext` compatible with SimpleW.


### Convert to SimpleW style

#### Minimal API

An ASP.NET Core Minimal API:

```csharp [ASP.NET Core]
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:8080");

var app = builder.Build();

app.MapGet("/api/users/{id:int}", (int id) => {
    return Results.Ok(new { id });
});

await app.RunAsync();
```

becomes a SimpleW delegate handler:

```csharp [SimpleW]
using System.Net;
using SimpleW;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/users/:id", (int id) => {
    return new { id };
});

await server.RunAsync();
```

SimpleW converts the `:id` route value to the handler parameter type. It also binds query-string values by parameter name. Unlike ASP.NET Core Minimal APIs, arbitrary handler parameters are not resolved from DI and complex request bodies are not bound automatically.

#### Controller

ASP.NET Core route conventions and action results need to become explicit SimpleW routes and return values.

```csharp [ASP.NET Core]
[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase {

    [HttpGet("{id:int}")]
    public ActionResult<User> Get(int id) {
        User? user = FindUser(id);
        return user == null ? NotFound() : Ok(user);
    }

}
```

```csharp [SimpleW]
[Route("/users")]
public sealed class UsersController : Controller {

    [Route("GET", "/:id")]
    public object Get(int id) {
        User? user = FindUser(id);
        return user == null ? Response.NotFound() : user;
    }

}
```

Register it with the application prefix:

```csharp
server.MapController<UsersController>("/api");
```

SimpleW does not expand tokens such as `[controller]`, apply ASP.NET route constraints such as `:int`, or run MVC filters. Use literal prefixes, typed handler parameters, middleware and handler metadata instead.

#### API map

| ASP.NET Core | SimpleW | Migration note |
| --- | --- | --- |
| `WebApplication` | `SimpleWServer` | Configure routes before `RunAsync()`. |
| `app.MapGet(...)` | `server.MapGet(...)` | The delegate style is intentionally similar. |
| `app.MapPost/Put/Delete/Patch(...)` | `server.Map("POST/PUT/DELETE/PATCH", ...)` | `MapPost(...)` is also available for POST. |
| `HttpContext` | `HttpSession` | Request and response are `session.Request` and `session.Response`. |
| `HttpContext.RequestAborted` | `session.RequestAborted` | Pass it to cancellable I/O. |
| `HttpContext.Items` | `session.Bag` | Data lives for the current request only. |
| `IResult` / `Results.Ok(value)` | Return `value` | Non-null values are serialized as JSON by the default result handler. |
| `Results.NotFound()` | `session.Response.NotFound()` | Return the `HttpResponse`. |
| `Results.File(path)` | `session.Response.File(path)` | Range requests are supported. |
| `Results.Redirect(url)` | `session.Response.Redirect(url)` | Return the `HttpResponse`. |
| `app.Use(...)` | `server.UseMiddleware(...)` | Adapt the delegate to `(session, next)`. |
| `UseStaticFiles()` | `UseStaticFilesModule(...)` | Configure its path and prefix. |
| `UseCors(...)` | `UseCorsModule(...)` | Recreate the policy explicitly. |
| `[ApiController]` / `ControllerBase` | `Controller` | Automatic MVC model validation and binding are not provided. |
| `[HttpGet(...)]` | `[Route("GET", ...)]` | Use `:name` for path parameters. |
| `HttpContext.User` | `session.Principal` | Authentication is configured separately. |

#### Middleware

ASP.NET Core middleware:

```csharp [ASP.NET Core]
app.Use(async (context, next) => {
    context.Response.Headers["X-Trace-Id"] = Guid.NewGuid().ToString("N");
    await next(context);
});
```

becomes:

```csharp [SimpleW]
server.UseMiddleware(async (session, next) => {
    session.Response.AddHeader("X-Trace-Id", Guid.NewGuid().ToString("N"));
    await next();
});
```

Registration order still matters. A SimpleW middleware either calls `next()` or short-circuits by returning/sending a response. See the [middleware guide](../guide/middleware.md).


### Keep the .NET Generic Host style

Use this path when the ASP.NET Core application already has valuable host-level setup such as `appsettings.json`, environment variables, `IHostedService`, Microsoft logging or an `IServiceCollection` registration graph.

Install the core package and the two official adapters:

```sh
dotnet add package SimpleW
dotnet add package SimpleW.Helper.Hosting
dotnet add package SimpleW.Helper.DependencyInjection
```

Then replace `WebApplication.CreateBuilder(...)` with `SimpleWHost.CreateApplicationBuilder(...)`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SimpleW;
using SimpleW.Helper.DependencyInjection;
using SimpleW.Helper.Hosting;

var builder = SimpleWHost.CreateApplicationBuilder(args)
                         .UseMicrosoftLogging();

builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.ConfigureSimpleW((services, server) => {
    server.UseDependencyInjection(services);
    server.MapController<UsersController>("/api");
});

var host = builder.Build();
await host.RunAsync();
```

Controllers can keep constructor injection after inheriting from the SimpleW `Controller` class and replacing their route attributes:

```csharp
[Route("/users")]
public sealed class UsersController : Controller {

    private readonly IUserRepository _users;

    public UsersController(IUserRepository users) {
        _users = users;
    }

    [Route("GET", "/:id")]
    public object Get(int id) {
        User? user = _users.Find(id);
        return user == null ? Response.NotFound() : user;
    }

}
```

The DI addon creates one `IServiceScope` per request and uses it to activate controllers. Configure `UseDependencyInjection(...)` before mapping controllers. Action parameters still come from route or query values, not from DI; inject services through the constructor or resolve them explicitly from the request services.

The hosting addon preserves the .NET Generic Host concepts documented by Microsoft in the [.NET Generic Host guide](https://learn.microsoft.com/dotnet/core/extensions/generic-host): application lifetime, configuration, logging, DI and hosted services. Read the [Hosting addon](../addons/helper-hosting.md) and [Dependency Injection addon](../addons/helper-dependency-injection.md) guides for configuration and lifetime details.


## From EmbedIO

SimpleW was inspired by the same small, embedded-server use cases as EmbedIO, but its types are not API-compatible. Treat this as a source migration rather than a package replacement. The examples below target the EmbedIO v3 API described in its [official repository](https://github.com/unosquare/embedio).

### Concept map

| EmbedIO | SimpleW | Notes |
| --- | --- | --- |
| `WebServer` | `SimpleWServer` | Construct it with an `EndPoint`, or with an IP address and port. |
| `WithUrlPrefix(...)` | `new SimpleWServer(address, port)` | SimpleW listens on one endpoint per server instance. |
| `WithWebApi("/api", ...)` | `MapController<T>("/api")` or `MapControllers<T>("/api")` | The mapping prefix is composed with controller and method routes. |
| `WebApiController` | `Controller` | Request state is available through `Request`, `Response`, `Session`, `Principal` and `Bag`. |
| `[Route(HttpVerbs.Get, "/path")]` | `[Route("GET", "/path")]` | SimpleW uses the HTTP method as a string. |
| `IHttpContext` | `HttpSession` | Use `session.Request` and `session.Response`. |
| `IWebModule` / `WithModule(...)` | `IHttpModule` / `UseModule(...)` | A SimpleW module installs routes, middleware and configuration. |
| `WithStaticFolder(...)` | `UseStaticFilesModule(...)` | Configure the filesystem path and URL prefix explicitly. |
| CORS module | `UseCorsModule(...)` | Recreate the policy and verify preflight responses. |
| WebSocket module | `UseWebSocketModule(...)` | The connection and message APIs differ; migrate the protocol handlers explicitly. |
| `RunAsync()` | `RunAsync()` | Await it to keep the process alive until shutdown. |

### Replace the server and controllers

An EmbedIO Web API application commonly starts like this:

```csharp [EmbedIO]
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

using var server = new WebServer(options => options
    .WithUrlPrefix("http://127.0.0.1:8080/")
    .WithMode(HttpListenerMode.EmbedIO))
    .WithWebApi("/api", module => module
        .WithController<HelloController>());

await server.RunAsync();

public sealed class HelloController : WebApiController {

    [Route(HttpVerbs.Get, "/hello")]
    public object Hello() {
        return new { message = "Hello World!" };
    }

}
```

The equivalent SimpleW application is:

```csharp [SimpleW]
using System.Net;
using SimpleW;

var server = new SimpleWServer(IPAddress.Loopback, 8080);
server.MapController<HelloController>("/api");

await server.RunAsync();

public sealed class HelloController : Controller {

    [Route("GET", "/hello")]
    public object Hello() {
        return new { message = "Hello World!" };
    }

}
```

The final route remains `GET /api/hello`. If an EmbedIO module owns a prefix, move that prefix either to `MapController(...)`, to a class-level `[Route(...)]`, or into the routes installed by a custom SimpleW module. Avoid applying the same prefix at more than one level.

### Migrate request and response code

Replace EmbedIO context helpers with the corresponding SimpleW request or response API:

| Need | SimpleW API |
| --- | --- |
| Method and path | `Request.Method`, `Request.Path` |
| Header | `Request.Headers.TryGetValue(name, out value)` |
| Query string | `Request.Query` or a handler parameter with the same name |
| Route value | `Request.RouteValues` or a handler parameter with the same name |
| JSON or URL-encoded body | Create the model, then call `Request.BodyMap(model)` |
| Small text body | `Request.BodyString` |
| Multipart body | `Request.BodyMultipart()` or `Request.BodyMultipartStream()` |
| JSON response | Return an object, or return `Response.Json(value)` |
| Status response | `Response.Status(code)`, `Response.NotFound()`, `Response.Unauthorized()`, and other helpers |
| File response | `Response.File(path)` |
| Request cancellation | `Session.RequestAborted` |
| Per-request state | `Session.Bag` |

SimpleW does not implicitly deserialize a JSON body into a controller parameter. Make that operation visible in the handler:

```csharp
[Route("POST", "/users")]
public object Create() {
    var input = new CreateUserRequest();

    if (!Request.BodyMap(input)) {
        return Response.Status(400).Text("A request body is required.");
    }

    return SaveUser(input);
}
```

See [Request](../guide/request.md), [Response](../guide/response.md) and [Handler](../guide/handler.md) for the complete execution model.

### Replace modules feature by feature

Do not translate every EmbedIO module into middleware. Use the SimpleW abstraction matching the responsibility:

- Use a [middleware](../guide/middleware.md) for request-wide behavior that can continue or short-circuit the pipeline.
- Use an [`IHttpModule`](../guide/module.md) to package routes, middleware and configuration as one reusable feature.
- Use the built-in [static files](../guide/staticfiles.md), [CORS](../guide/cors.md) and [WebSocket](../guide/websockets.md) modules when they match the feature.
- Use handler [metadata attributes](../guide/handler-attribute.md) plus middleware for declarative authorization or filtering rules.

::: warning Check these differences carefully
If the EmbedIO application targets .NET Framework or Mono, retarget the application or isolate its reusable code first: the current SimpleW package targets .NET 8, .NET 9 and .NET 10. Also review TLS setup, route-pattern syntax, session behavior, WebSocket messages and any custom module ordering instead of assuming identical defaults.
:::


## Final migration checklist

Before removing the previous server implementation, verify:

- Every public method and path, including trailing-slash behavior and route prefixes.
- Query, route, JSON, form and multipart binding, especially missing or invalid values.
- Status codes, content types, JSON property names, headers, cookies and redirects.
- Middleware order, short-circuit behavior and exception handling.
- Authentication, authorization, CORS and proxy-forwarded client information.
- Request body limits, upload streaming and file-path sanitization.
- Static-file cache behavior and SPA fallback routes.
- WebSocket handshake, message format, close behavior and reconnect logic.
- HTTPS certificates, reverse proxy settings and the single-listener-per-server model.
- Graceful shutdown and cancellation of long-running request work.
- Logs, metrics and traces required by production operations.

Once these checks pass, remove the old web package and any adapters that are no longer referenced.
