# Middleware

SimpleW provides a lightweight and flexible middleware pipeline.
A middleware allows you to intercept an HTTP request, execute custom logic, and decide whether
to continue the pipeline or short-circuit the response.

## Registering a Middleware

You can register a middleware using the [`UseMiddleware`](../reference/simplewserver.md#usemiddleware) method :

```csharp
server.UseMiddleware(HttpMiddleware middleware);
```

A middleware is a delegate with the following [signature](../reference//httpmiddleware.md) :

```csharp
ValueTask HttpMiddleware(HttpSession session, Func<ValueTask> next)
```

- `session` : Represents the current HTTP session (request and response).
- `next` : Invokes the next middleware in the pipeline.

Calling `next()` continues the request processing.
If `next()` is not called, the pipeline stops and the response is considered handled.


## Examples

### Request Logging

This middleware measures the execution time of a request and logs basic request information.

```csharp
// use middleware for logging
server.UseMiddleware(static async (session, next) => {
    // start a timer
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try {
        await next(); // continue the pipeline (and so send the response)
    }
    finally {
        // back from pipeline (the response has been sent)
        sw.Stop();
        Console.WriteLine($"[{DateTime.UtcNow:O}] {session.Request.Method} {session.Request.Path} in {sw.ElapsedMilliseconds} ms");
    }
});
```

Notes :
- `await next()` ensures the request continues through the pipeline.
- The finally block guarantees logging even if an exception occurs downstream.


### Example: Simple Firewall / Authentication

```csharp
// minimal api
server.MapGet("/", () => {
    return new { message = "Hello World !" };
});
server.MapGet("/api/test", () => {
    return new { message = "authenticated" };
});

// use middleware as firewall/authenticate
server.UseMiddleware(static (session, next) => {
    // check if the user is authorized ?
    if (session.Request.Path.StartsWith("/api", StringComparison.Ordinal)) {
        if (!session.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != "secret") {
            // stop the pipeline here by sending a 401
            return session.Unauthorized("You're authorized in this area");
        }
    }
    // continue the pipeline
    return next();
});
```

Behavior :
- Requests targeting "/api/test" require a valid X-Api-Key header.
- Unauthorized requests are immediately rejected with a 401 Unauthorized response.
- Authorized requests continue through the middleware pipeline.


## Execution Order

Middlewares are executed in the order they are registered.
Each middleware decides whether the next one is executed by calling `next()`.

This makes it easy to implement :
- Logging
- Authentication / authorization
- Rate limiting
- Request filtering
- Metrics collection



## Bag (Per-Request Storage)

SimpleW provides a lightweight **per-request storage container** called the **Bag**.

The Bag allows middlewares, handlers, and controllers to **share data during the lifetime of a single HTTP request**.

It is conceptually similar to `HttpContext.Items` in ASP.NET Core.

The Bag is attached to the [`HttpSession`](../reference/httpsession.md#bag) and is automatically reset for every request.

### Why Bags Exist

During request processing, different components may need to share intermediate data.

Typical examples :
- Authentication modules storing the authenticated user
- Middleware computing a correlation ID
- Firewall modules attaching match results
- Telemetry modules recording request metadata

Without a shared storage, developers would need to recompute data or modify unrelated objects.

The Bag provides a simple and explicit solution.


### Lifetime

A Bag exists **only for the duration of a single HTTP request**.

Characteristics :

- Created lazily (only when first used)
- Cleared automatically between requests
- Shared across the entire pipeline

Lifecycle :
```
Incoming request
↓
Middleware A
↓
Middleware B
↓
Handler / Controller
↓
Response sent
↓
Bag cleared
```

This ensures **no data leaks between requests**, even when HTTP keep-alive is used.


### Accessing the Bag

The Bag is available from `HttpSession`:

```csharp
session.Bag
```

**Storing Values**

You can store any object using `Set`.

```csharp
session.Bag.Set("trace.id", Guid.NewGuid());
session.Bag.Set("auth.user", user);
```


### Reading Values

Use TryGet when the value may not exist :

```csharp
if (session.Bag.TryGet<string>("trace.id", out var traceId)) {
    Console.WriteLine(traceId);
}
```

Or Get when the value must exist :

```csharp
var user = session.Bag.Get<User>("auth.user");
```

### Middleware Example

Middleware can write to the Bag :

```csharp
server.UseMiddleware(static (session, next) => {
    session.Bag.Set("trace.id", Guid.NewGuid().ToString("N"));
    return next();
});
```

Another middleware can read the value :

```csharp
server.UseMiddleware(static async (session, next) => {
    if (session.Bag.TryGet<string>("trace.id", out var traceId)) {
        session.Response.Header("X-Trace-Id", traceId);
    }
    await next();
});
```


### Handler Example

Handlers can also access the Bag :

```csharp
server.MapGet("/api/test", (HttpSession session) => {
    var traceId = session.Bag.Get<string>("trace.id");
    return new { traceId };
});
```


### Controller Example

Controllers expose the Bag directly :

```csharp
public class TestController : Controller {

    [Route("GET", "/api/test")]
    public object Test() {
        var traceId = Bag.Get<string>("trace.id");
        return new { traceId };
    }
}
```

### Performance Considerations

The Bag is designed to have **minimal impact on request performance**.

Key properties :
- Lazy allocation (no cost if unused)
- Reused across keep-alive requests
- Internal dictionary cleared instead of reallocated

However, because the Bag stores values as `object` :
- **value types are boxed when stored**
- **unboxed when retrieved**

This is usually negligible, but storing large numbers of small value types is discouraged.

Recommended usage :
- Store reference types (objects)
- Store intermediate request data
- Avoid using the Bag as a large data container


## Best Practices

- Keep middleware small and focused on a single responsibility.
- Always call next() unless you intentionally want to stop request processing.
- Prefer static lambdas when possible to avoid unnecessary allocations.
