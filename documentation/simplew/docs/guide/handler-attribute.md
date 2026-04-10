# Handler Attribute

## Overview

SimpleW allows you to attach **custom metadata** to handlers using attributes.

Mental Model

> An attribute is only metadata.<br />
> A classic middleware reads that metadata and decides what to do.

This keeps the design simple :

- `attribute` = declarative data
- `middleware` = runtime behavior
- `session` = access point for the matched handler metadata


## Why This Exists

Sometimes a route needs extra information that is not part of the path, the method, or the handler parameters.

Examples :

- "this handler requires the `x-api-key` header"
- "this handler belongs to the `admin` area"
- "this handler should be audited"
- "this handler uses the `Bearer` auth scheme"

If that information is stored directly in middleware code, it quickly becomes hard to reuse and hard to read.

Handler metadata gives you a clean model :

1. declare intent on the handler with an attribute
2. read that intent from middleware
3. apply behavior centrally


## How It Works

SimpleW collects attributes implementing `IHandlerMetadata` when routes are registered.

Those metadata are then exposed during the request through :

- [`session.Metadata`](../reference/httpsession.md#metadata)
- [`Controller.Metadata`](../reference/httpsession.md#metadata)

At runtime, a classic middleware can inspect the matched handler metadata :

```csharp
server.UseMiddleware((session, next) => {
    IReadOnlyList<RequireHeaderAttribute> rules = session.Metadata.GetAll<RequireHeaderAttribute>();

    if (rules.Count == 0) {
        return next();
    }

    return session.Response.Status(401).Text("Missing header").SendAsync();
});
```

## Metadata Flow

```text
[Attribute on controller]      [Attribute on method]
             \                         /
              \                       /
               v                     v
             Route registration collects IHandlerMetadata
                               |
                               v
                      Matched request exposes
                session.Metadata / Controller.Metadata
                               |
                               v
                    Middleware reads metadata rules
                         |                  |
                      continue         deny / alter behavior
```


## Creating a Custom Metadata Attribute

To create custom handler metadata, implement `IHandlerMetadata` on your attribute.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class RequireHeaderAttribute : Attribute, IHandlerMetadata
{
    public string Name { get; }
    public string? Value { get; }

    public RequireHeaderAttribute(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }
}
```

The attribute should stay **immutable**.
It should only carry data.

Recommended :

- constructor parameters
- read-only properties
- strings, booleans, roles, flags, names

Avoid :

- mutable state
- counters
- caches
- per-request logic inside the attribute


## Reading Metadata From Middleware

Once the route is matched, the metadata is available from `session.Metadata`.

### `Has<T>()`

```csharp
if (session.Metadata.Has<RequireHeaderAttribute>()) {
    // at least one metadata item exists
}
```

### `Get<T>()`

Returns the most specific metadata item.
If both a controller and a method define the same type, the method wins.

```csharp
RequireHeaderAttribute? current = session.Metadata.Get<RequireHeaderAttribute>();
```

### `GetAll<T>()`

Returns every metadata item of the requested type.

```csharp
IReadOnlyList<RequireHeaderAttribute> rules = session.Metadata.GetAll<RequireHeaderAttribute>();
```


## Complete Example

This example shows the full flow :

1. create metadata attributes
2. decorate handlers
3. use a normal middleware to enforce the rules

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseMiddleware((session, next) => {
    IReadOnlyList<RequireHeaderAttribute> requiredHeaders = session.Metadata.GetAll<RequireHeaderAttribute>();

    for (int i = 0; i < requiredHeaders.Count; i++) {
        RequireHeaderAttribute rule = requiredHeaders[i];

        if (!session.Request.Headers.TryGetValue(rule.Name, out string? value)) {
            return session.Response.Status(401).Json(new {
                error = "Missing required header",
                header = rule.Name
            }).SendAsync();
        }

        if (rule.Value != null && !string.Equals(value, rule.Value, StringComparison.Ordinal)) {
            return session.Response.Status(403).Json(new {
                error = "Invalid header value",
                header = rule.Name,
                expected = rule.Value
            }).SendAsync();
        }
    }

    return next();
});

server.MapController<MetadataDemoController>();
server.MapGet("/metadata-demo/delegate", MetadataDemoHandlers.Hello);

[Route("/metadata-demo")]
public class MetadataDemoController : Controller
{
    [Route("GET", "/public")]
    public object Public()
    {
        return new { message = "public endpoint" };
    }

    [RequireHeader("x-demo-key", "secret")]
    [Route("GET", "/secure")]
    public object Secure()
    {
        return new { message = "protected endpoint" };
    }
}

public static class MetadataDemoHandlers
{
    [RequireHeader("x-demo-key", "secret")]
    public static object Hello()
    {
        return new { message = "delegate endpoint" };
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public class RequireHeaderAttribute : Attribute, IHandlerMetadata
{
    public string Name { get; }
    public string? Value { get; }

    public RequireHeaderAttribute(string name, string? value = null)
    {
        Name = name;
        Value = value;
    }
}
```


## Controller Access

Controllers expose the same metadata collection through `Metadata`.

```csharp
[Route("/demo")]
[AuditTag("controller")]
public class DemoController : Controller
{
    [AuditTag("method")]
    [Route("GET", "/tags")]
    public object Tags()
    {
        return new {
            tags = Metadata.GetAll<AuditTagAttribute>().Select(t => t.Name).ToArray()
        };
    }
}
```

This can be useful when the handler itself needs to inspect the attached metadata.


## Merge Rules

Metadata is collected in this order :

1. class metadata
2. method metadata

That means :

- `GetAll<T>()` returns class items first, then method items
- `Get<T>()` returns the most specific item, usually the method one when present


## Delegate Support

Handler metadata also works with delegate handlers, as long as the attribute is applied to a real method.

This works :

```csharp
server.MapGet("/demo", DemoHandlers.Hello);

public static class DemoHandlers
{
    [RequireHeader("x-demo-key", "secret")]
    public static object Hello()
    {
        return new { ok = true };
    }
}
```

Inline lambdas cannot be decorated with C# attributes, so metadata is not a good fit for anonymous inline handlers.

For those cases, prefer :

- controller methods
- static methods
- instance methods passed as method groups


## Request Lifetime

Handler metadata is :

- collected once when routes are registered
- reused across requests
- exposed only for the matched request
- reset between requests

This means middlewares can safely read `session.Metadata` like any other per-request data source.


## Best Practices

- Keep metadata attributes small and immutable.
- Put all behavior in middleware, not in the attribute.
- Use metadata for declaration, not execution.
- Prefer explicit names like `RequireHeaderAttribute`, `AuditTagAttribute`, `AuthSchemeAttribute`.
- Use [`Middleware`](./middleware.md) when you need runtime logic.


## Summary

Handler metadata gives you an ASP.NET-like declarative style without adding a special execution model.

The pattern is :

1. create an attribute implementing `IHandlerMetadata`
2. decorate a controller method or delegate method
3. read `session.Metadata` from a classic middleware
4. continue or short-circuit the request

This makes custom auth-like patterns easy to build while keeping the library core simple and predictable.
