# Principal

The **Principal system** in SimpleW represents the authenticated user and all associated identity data.

It is composed of three core types:

- `HttpPrincipal` -> the user
- `HttpIdentity` -> the identity of the user
- `IdentityProperty` -> extensible key/value metadata

Conceptually:

> The Principal system is the single source of truth for user identity in SimpleW.

::: info
If you want the conceptual overview first, start with the [Security guide](./security.md).
This page focuses on how those concepts are represented inside SimpleW through `HttpPrincipal`, `HttpIdentity`, and middleware.
:::

## Architecture Overview

```text
HttpPrincipal
  -> HttpIdentity
     -> Identifier
     -> Name
     -> Email
     -> Roles
     -> Properties (IdentityProperty[])
```

:::: info
`HttpPrincipal` is similar in spirit to the ASP.NET Core principal, except that:

- it contains only one `HttpIdentity`
- claims-like custom values are exposed as `IdentityProperty`
::::

### HttpPrincipal

[`HttpPrincipal`](../reference/httpprincipal.md) represents the **current user**.

It contains exactly **one identity**.

Key properties:

- `Identity` -> the underlying identity
- `IsAuthenticated` -> shortcut to `Identity.IsAuthenticated`
- `Name`, `Email` -> shortcuts
- `IsInRole(string role)`
- `IsInRoles(string rolesCsv)`
- `Get(string key)` -> access custom properties

Example:

```csharp
if (session.Principal.IsAuthenticated) {
    Console.WriteLine(session.Principal.Name);
}

if (session.Principal.IsInRole("admin")) {
    // authorized
}
```

**Mental Model**

> Principal = "who is making this request"

### HttpIdentity

[`HttpIdentity`](../reference/httpidentity.md) represents **how the user is identified**.

Key properties:

- `IsAuthenticated`
- `AuthenticationType` (for example `"Bearer"` or `"Cookie"`)
- `Identifier` (unique ID, usually `sub`)
- `Name`
- `Email`
- `Roles` (`IReadOnlyCollection<string>`)
- `Properties` (`IReadOnlyCollection<IdentityProperty>`)

Example:

```csharp
var identity = new HttpIdentity(
    isAuthenticated: true,
    authenticationType: "Bearer",
    identifier: "user-123",
    name: "John",
    email: "john@example.com",
    roles: new[] { "admin", "user" },
    properties: new[] {
        new IdentityProperty("tenant_id", "acme"),
        new IdentityProperty("plan", "pro")
    }
);
```

**Mental Model**

> Identity = "how we know who the user is"

### IdentityProperty

[`IdentityProperty`](../reference/identityproperty.md) is a **flexible key/value pair** used to store custom data.

Example:

```csharp
new IdentityProperty("tenant_id", "acme");
new IdentityProperty("feature_flag", "beta");
```

Access:

```csharp
string? tenant = session.Principal.Get("tenant_id");
```

Use cases:

- multi-tenant systems
- feature flags
- permissions
- custom claims (JWT, OAuth, etc.)

**Mental Model**

> IdentityProperty = "everything that does not belong to core identity fields"

## Accessing the Principal

The principal is available in both execution contexts:

- `HttpSession.Principal`
- `Controller.Principal`

This follows the same pattern as `Request` and `Response`.

:::: code-group

```csharp [From Session]
server.MapGet("/me", (HttpSession session) => {
    return new {
        id = session.Principal.Identity.Identifier,
        name = session.Principal.Name,
        email = session.Principal.Email,
        roles = session.Principal.Roles,
        tenant = session.Principal.Get("tenant_id")
    };
});
```

```csharp [From Controller]
[Route("/user")]
public class UserController : Controller {

    [Route("GET", "/me")]
    public object Me() {
        return new {
            id = Principal.Identity.Identifier,
            name = Principal.Name,
            email = Principal.Email,
            roles = Principal.Roles,
            properties = Principal.Identity.Properties
        };
    }
}
```

::::

Accessing `Principal` does not imply that the caller is authenticated.
Use `IsAuthenticated` and the authorization checks described below before returning protected data.

## Setting the Principal

Until an authenticated identity is provided, `session.Principal` resolves to `HttpPrincipal.Anonymous`.

There are two ways to provide the principal for a request:

- assign `session.Principal` directly, usually from an authentication middleware
- configure a lazy `PrincipalResolver`


### Direct Assignment from Middleware

A middleware can validate credentials and assign the resulting identity before the handler runs.

The example below assumes that `X-User` and `X-Roles` are supplied by a trusted upstream component.

```csharp
server.UseMiddleware(async (session, next) => {
    if (session.Request.Headers.TryGetValue("X-User", out string? user) && !string.IsNullOrWhiteSpace(user)) {
        string[]? roles = null;
        if (session.Request.Headers.TryGetValue("X-Roles", out string? rawRoles) && !string.IsNullOrWhiteSpace(rawRoles)) {
            roles = rawRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        session.Principal = new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: "Header",
            identifier: user,
            name: user,
            email: null,
            roles: roles,
            properties: null
        ));
    }

    await next().ConfigureAwait(false);
});
```

The assigned principal belongs to the current request only.
The session resets it before dispatching the next request, and a direct assignment replaces any value that was resolved earlier in the current request.

The principal can be created from any trusted authentication source, whether it is built into SimpleW or specific to your application.

::: info
Concrete integration examples include JWT ([helper](../addons/helper-jwt.md)/[service](../addons/service-jwt.md)), Basic authentication ([helper](../addons/helper-basicauth.md)/[service](../addons/service-basicauth.md)), OpenID ([helper](../addons/helper-openid.md) or a [service](../addons/service-openid.md)), [TLS client certificates](./tls-certificates.md#mapping-mtls-to-httpprincipal)
:::


### Using ConfigurePrincipalResolver

[`ConfigurePrincipalResolver`](../reference/simplewserver.md#configureprincipalresolver) lets the server build the principal lazily from the request.

```csharp
server.ConfigurePrincipalResolver(session => {
    return new HttpPrincipal(new HttpIdentity(
        isAuthenticated: true,
        authenticationType: "Custom",
        identifier: "user-123",
        name: "John",
        email: null,
        roles: new[] { "admin" },
        properties: null
    ));
});
```

The resolver is synchronous and runs on the first access to `session.Principal`, at most once per request.
Returning `null` uses `HttpPrincipal.Anonymous`.

This approach is a good fit when identity mapping is simple, synchronous, and only needs to run if the principal is actually consumed.
Prefer a middleware when authentication requires asynchronous work, may produce a response directly, or needs precise control over the request pipeline.

### Choosing an Approach

| Approach | Best for |
| --- | --- |
| Assignment from middleware | Complete authentication flows and request-pipeline control |
| `ConfigurePrincipalResolver` | Simple, synchronous, and lazy principal resolution |

## Checking the Principal

Once the principal has been set, handlers, controllers, modules, and middlewares can use it to decide whether the request is allowed.

### Checking Authentication

Use `IsAuthenticated` when an endpoint only requires a known caller.
An unauthenticated request normally receives `401 Unauthorized`.

:::: code-group

```csharp [From Session]
server.MapGet("/account", (HttpSession session) => {
    if (!session.Principal.IsAuthenticated) {
        return session.Response.Unauthorized("Not authenticated");
    }

    return new { user = session.Principal.Name };
});
```

```csharp [From Controller]
[Route("/account")]
public class AccountController : Controller {

    [Route("GET", "/")]
    public object Get() {
        if (!Principal.IsAuthenticated) {
            return Response.Unauthorized("Not authenticated");
        }

        return new { user = Principal.Name };
    }
}
```

::::

### Checking Roles

Use `IsInRole` for one role and `IsInRoles` when any role in a comma-separated expression is accepted.

```csharp
bool isAdmin = session.Principal.IsInRole("admin");
bool canManage = session.Principal.IsInRoles("admin,manager");
```

`"admin,manager"` means `admin OR manager`.

Check authentication first.
If the caller is authenticated but does not have the required role, the response is normally `403 Forbidden` rather than `401 Unauthorized`.

### Checking Identity Properties

Custom identity properties can participate in application-specific decisions:

```csharp
string? tenant = session.Principal.Get("tenant_id");
bool hasProPlan = session.Principal.Has("plan", "pro");
```

These checks are useful for tenant boundaries, feature access, or other application rules.
They do not enforce an authorization policy automatically.

### Declarative Authorization

SimpleW provides two core metadata attributes that work naturally with `HttpPrincipal`:

- `[AllowAnonymous]` marks a handler as public and lets auth middleware bypass it
- `[RequireRole("admin")]` declares that the current principal must match the requested role expression

`RequireRoleAttribute` relies on the same role matching semantics as `HttpPrincipal.IsInRoles(...)`:

- `"admin"` means the principal must be in the `admin` role
- `"admin,manager"` means `admin OR manager`
- multiple `[RequireRole(...)]` attributes describe separate requirements

Those attributes are declarative only.
They do not authenticate the request by themselves.
Modules such as `SimpleW.Service.Jwt`, or your own middleware, read them through `session.Metadata` and enforce them.

Example:

```csharp
[Route("/admin")]
public class AdminController : Controller {

    [AllowAnonymous]
    [Route("GET", "/health")]
    public object Health() {
        return new { ok = true };
    }

    [RequireRole("admin")]
    [Route("GET", "/dashboard")]
    public object Dashboard() {
        return new {
            user = Principal.Name,
            roles = Principal.Roles
        };
    }

    [RequireRole("admin,manager")]
    [RequireRole("billing")]
    [Route("GET", "/billing")]
    public object Billing() {
        return new { ok = true };
    }
}
```

In this example:

- `/admin/health` stays public
- `/admin/dashboard` requires a principal in role `admin`
- a middleware that enforces every attribute treats `/admin/billing` as `(admin OR manager) AND billing`

### Centralizing Checks in Middleware

For shared authorization rules, use a middleware or authentication module so the policy is applied before business logic runs.

The following middleware assumes that an earlier middleware, a module, or `PrincipalResolver` provides the principal:

```csharp
server.UseMiddleware(async (session, next) => {
    if (session.Metadata.Has<AllowAnonymousAttribute>()) {
        await next().ConfigureAwait(false);
        return;
    }

    if (!session.Principal.IsAuthenticated) {
        await session.Response.Unauthorized().SendAsync().ConfigureAwait(false);
        return;
    }

    IReadOnlyList<RequireRoleAttribute> requiredRoles = session.Metadata.GetAll<RequireRoleAttribute>();
    foreach (RequireRoleAttribute requirement in requiredRoles) {
        if (!session.Principal.IsInRoles(requirement.Role)) {
            await session.Response
                         .Status(403)
                         .Json(new {
                             ok = false,
                             error = "forbidden",
                             role = requirement.Role
                         })
                         .SendAsync()
                         .ConfigureAwait(false);
            return;
        }
    }

    await next().ConfigureAwait(false);
});
```

Checking `Principal` directly inside a handler or controller is appropriate for endpoint-specific behavior.
Centralizing shared rules avoids duplicating the same checks across endpoints.
