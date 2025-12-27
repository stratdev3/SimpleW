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


## Best Practices

- Keep middleware small and focused on a single responsibility.
- Always call next() unless you intentionally want to stop request processing.
- Prefer static lambdas when possible to avoid unnecessary allocations.
