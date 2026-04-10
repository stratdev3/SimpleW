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

## HttpPrincipal

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

## HttpIdentity

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

## IdentityProperty

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
    if (!session.Principal.IsAuthenticated) {
        return session.Response.Unauthorized("Not authenticated");
    }

    return new {
        id = session.Principal.Identity.Identifier,
        name = session.Principal.Name,
        roles = session.Principal.Identity.Roles
    };
});
```

```csharp [From Controller]
[Route("/user")]
public class UserController : Controller {

    [Route("GET", "/me")]
    public object Me() {
        if (!Principal.IsAuthenticated) {
            return Response.Unauthorized("Not authenticated");
        }

        return new {
            id = Principal.Identity.Identifier,
            name = Principal.Name
        };
    }
}
```

::::

For endpoint-specific behavior, checking `Principal` directly inside a handler or controller is perfectly fine.

For shared authentication or authorization rules, prefer a middleware so the policy is applied once, early in the pipeline, before business logic runs.

## Setting and Checking the Principal

In most applications, the **best place** to set and check the principal is a **middleware**.

Why middleware is usually the right place:

- it runs early for every request
- it can restore `session.Principal` from headers, cookies, tokens, certificates, or any custom source
- it can stop the pipeline immediately with `401` or `403`
- it keeps handlers and controllers focused on business logic
- it can read `session.Metadata`, including `[AllowAnonymous]` and `[RequireRole(...)]`

Example:

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

This pattern is the usual place to:

- authenticate the request
- populate `session.Principal`
- apply shared access-control rules
- short-circuit unauthorized requests

Handlers and controllers can still read `Principal`, but they no longer need to duplicate the same authorization checks everywhere.

## Other Ways to Set the Principal

SimpleW also exposes lower-level alternatives when middleware is not the right fit.

### ConfigurePrincipalResolver

[`ConfigurePrincipalResolver`](../reference/simplewserver.md#configureprincipalresolver) is useful when you want the server to build the principal lazily from the request, without writing a dedicated auth middleware.

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

This is a good fit for:

- simple global identity mapping
- infrastructure scenarios
- low-level integrations where you only need to resolve the user

It is usually **not** the best place to implement full authorization policy.
For that, middleware stays more explicit and easier to compose.

### Direct Assignment

You can also assign the principal manually when a specific flow has just authenticated the user.

```csharp
session.Principal = new HttpPrincipal(new HttpIdentity(
    isAuthenticated: true,
    authenticationType: "Custom",
    identifier: "user-123",
    name: "John",
    email: null,
    roles: new[] { "admin" },
    properties: null
));
```

This is useful for:

- login callbacks
- custom challenge flows
- one-off technical handlers

:::: info
The principal resolver is lazy and only runs when the principal is actually needed.
This avoids unnecessary work and improves performance.
::::

**Mental Model**

> Middleware = "authenticate and authorize the request"

> PrincipalResolver = "build the user from the request when needed"

## Declarative Authorization Metadata

SimpleW also provides two core metadata attributes that work naturally with `HttpPrincipal`:

- `[AllowAnonymous]` marks a handler as public and lets auth middleware bypass it
- `[RequireRole("admin")]` declares that the current principal must match the requested role expression

`RequireRoleAttribute` relies on the same role matching semantics as `HttpPrincipal.IsInRoles(...)`:

- `"admin"` means the principal must be in the `admin` role
- `"admin,manager"` means `admin OR manager`
- multiple `[RequireRole(...)]` attributes can be combined on the same handler and are evaluated independently by middleware

Those attributes are declarative only.
They do not authenticate the request by themselves.
Instead, modules and middlewares such as `SimpleW.Service.Jwt` or your own custom middleware read them through `session.Metadata`.

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
- `/admin/billing` typically means `(admin OR manager) AND billing`, depending on the middleware enforcing the metadata

## Real Example of Principal integration

Examples of `HttpPrincipal` integration:

- [SimpleW.Helper.Jwt](../addons/helper-jwt.md) and [SimpleW.Service.Jwt](../addons/service-jwt.md)
- [SimpleW.Helper.BasicAuth](../addons/helper-basicauth.md) and [SimpleW.Service.BasicAuth](../addons/service-basicauth.md)
- [SimpleW.Helper.OpenID](../addons/helper-openid.md) and [SimpleW.Service.OpenID](../addons/service-openid.md)
- [TLS Client Certificate](./ssl-certificate.md#mapping-mtls-to-httpprincipal)
