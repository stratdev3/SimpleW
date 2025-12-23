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

::: code-group

<<< @/snippets/middleware-logging.cs#snippet{18-30 csharp:line-numbers} [program.cs]

:::


Notes :
- `await next()` ensures the request continues through the pipeline.
- The finally block guarantees logging even if an exception occurs downstream.

### Example: Simple Firewall / Authentication

::: code-group

<<< @/snippets/middleware-firewall.cs#snippet{21-32 csharp:line-numbers} [program.cs]

:::

Behavior :
- Requests targeting http://localhost:2015/api/test require a valid X-Api-Key header.
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
