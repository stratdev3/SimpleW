# HttpMiddleware

It is used by [`SimpleWServer.UseMiddleware`](./simplewserver.md#usemiddleware) to add a new Middleware.


## Signature

A middleware is a delegate with the following signature :

```csharp
/// <summary>
/// Delegate for Middleware
/// </summary>
/// <param name="session"></param>
/// <param name="next"></param>
/// <returns></returns>
public delegate ValueTask HttpMiddleware(HttpSession session, Func<ValueTask> next);
```

- `session` : Represents the current HTTP session (request and response).
- `next` : Invokes the next middleware in the pipeline.

Calling `next()` continues the request processing.
If `next()` is not called, the pipeline stops and the response is considered handled.