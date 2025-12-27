# Routing

Routing is the mechanism that connects an incoming HTTP request to **the correct [`handler`](./handler.md)**.

SimpleW routing is intentionally :

- **Explicit** – routes are declared, not inferred
- **Predictable** – no hidden conventions
- **Fast** – exact matches first, patterns only when needed

Routing answers a single question :

> Given an HTTP method and a path, which handler should run?


## How a Route is Built

A final route path is built by concatenating up to three parts :
- **Server prefix** (defined when mapping controllers or routes)
- **Controller route prefix** (optional)
- **Method route path**

Example :

```csharp
server.MapControllers<Controller>("/api");

[Route("/users")]
public class UserController : Controller {

    [Route("GET", "/list")]
    public object List() { 
        ...
    }
}
```

Final route:

```
GET /api/users/list
```

Each level is purely compositional. There is no magic.


## Route Declaration Model

Routes are declared using the [`[Route]`](../reference/routeattribute.md) attribute.

A route always defines :
- An HTTP method (GET, POST, …)
- A path

```csharp
[Route("GET", "/index")]
public object Index() { ... }
```

### Controller-Level Routes

A controller may define a **route prefix** :

```csharp
[Route("/test")]
public class TestController : Controller { ... }
```

All method routes inside the controller inherit this prefix.

### Absolute Routes

A method route can opt out of the controller prefix :

```csharp
[Route("GET", "/home", isAbsolutePath: true)]
```

This route is evaluated from the root, ignoring the controller prefix.


## Multiple Routes per Handler

A single handler may be bound to multiple routes :

```csharp
[Route("POST", "/delete")]
[Route("POST", "/remove")]
public object Delete() { ... }
```

Each attribute registers a distinct route pointing to the same handler.


## Route Matching Strategy

Routing follows a deterministic priority order :
1. **Exact match** (method + path)
2. **Pattern match** (parameters, wildcards)
3. **Fallback handler** (if configured)
4. **404 Not Found**

This guarantees :
- O(1) lookup for common routes
- No ambiguity between exact and pattern routes


## Path Parameters

Routes may declare **path parameters** :

```csharp
[Route("GET", "/users/:id")]
public object GetUser(int id) { ... }
```

Characteristics :
- Parameters are mandatory
- Values are mapped by name
- Values are converted to the target type

If conversion fails, the handler execution fails.

### Mental Model

> A path parameter is part of the route identity, not optional input.


## Catch-All Routes (Wildcards)

Routes may include wildcards :

```csharp
[Route("GET", "/*")]
```

This matches any path below the current prefix.
Typical use cases :
- Maintenance mode
- API deprecation
- Custom 404 / 503 handling

::: warning
Catch-all routes should be used carefully, as they shadow more specific routes.
:::


## Route Documentation and Descriptions

Routes are part of your public API and should be self-documented.

SimpleW allows you to attach a **description** to a route via the `Route` attribute.

Providing descriptions is strongly recommended because it :
- Improves readability of the codebase
- Makes intent explicit for future maintainers
- Helps tooling and documentation generators
- Serves as in-code API documentation

Example

```csharp
[Route("GET", "/users/:id", Description = "Returns a user by its unique identifier")]
public object GetUser(int id) {
    ...
}
```

## Listing Registered Routes

SimpleW exposes all registered routes through the router.
You can inspect them at runtime using the [`Router.Routes`](../reference/router.md#routes) property :

```csharp
var routes = server.Router.Routes;
```

This allows you to:
- List all registered routes
- Build diagnostics or debug endpoints
- Generate documentation or tooling
- Verify route registration at startup

Each route entry contains the HTTP method, resolved path, and associated handler metadata.


## API Versioning via Routing

Routing is the primary mechanism for API versioning.

```csharp
server.MapControllers<Controller>("/api/v1");
server.MapControllers<Controller>("/api/v2");
```

Each version :
- Has its own route space
- Can evolve independently
- Can reuse controller names safely

Versioning through routing keeps :
- URLs explicit
- Backward compatibility under control
