# Principal, Identity & IdentityProperty

The **Principal system** in SimpleW represents the authenticated user and all associated identity data.

It is composed of three core types :

- `HttpPrincipal` → the user
- `HttpIdentity` → the identity of the user
- `IdentityProperty` → extensible key/value metadata

Conceptually :

> The Principal system is the single source of truth for user identity in SimpleW.

## Architecture Overview

```
HttpPrincipal
    └── HttpIdentity
            ├── Identifier
            ├── Name
            ├── Email
            ├── Roles
            └── Properties (IdentityProperty[])
```


## HttpPrincipal

[`HttpPrincipal`](../reference/httpprincipal.md) represents the **current user**.

It contains exactly **one identity**.
Key properties :
- `Identity` → the underlying identity
- `IsAuthenticated` → shortcut to `Identity.IsAuthenticated`
- `Name`, `Email` → shortcuts
- `IsInRole(string role)`
- `IsInRoles(string rolesCsv)`
- `Get(string key)` → access custom properties


Example

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

**Key properties**
- `IsAuthenticated`
- `AuthenticationType` (e.g. `"Bearer"`, `"Cookie"`)
- `Identifier` (unique ID, usually `sub`)
- `Name`
- `Email`
- `Roles` (`IReadOnlyCollection<string>`)
- `Properties` (`IReadOnlyCollection<IdentityProperty>`)

Example

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

Example

```csharp
new IdentityProperty("tenant_id", "acme");
new IdentityProperty("feature_flag", "beta");
```

Access

```csharp
string? tenant = session.User.Get("tenant_id");
```

Use cases :
- multi-tenant systems
- feature flags
- permissions
- custom claims (JWT, OAuth, etc.)

**Mental Model**

> IdentityProperty = "everything that does not belong to core identity fields"


## Accessing the Principal

The principal is available in both execution contexts :
- `HttpSession.Principal`
- `Controller.Principal`

This follows the same pattern as `Request` and `Response`.


::: code-group

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

:::


## Setting the Principal

There are **two ways** to define the principal.

### 1. Direct assignment (manual)

You can directly set the user on the session.

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

When to use :
- manual authentication
- test scenarios
- custom pipelines


### 2. ConfigurePrincipalResolver (recommended)

The recommended approach is to define a **resolver** at the server level.

```csharp
server.ConfigurePrincipalResolver(session => {

    string? token = session.Request.Headers.Authorization;

    if (string.IsNullOrWhiteSpace(token)) {
        return null;
    }

    if (!JwtBearerHelper.TryValidateToken(options, token, out HttpPrincipal? principal, out _)) {
        return null;
    }

    return principal;
});
```

::: info
The principal is resolved only when needed by lazy loading.
This avoids unnecessary work and improves performance.
:::

**Mental Model**

> PrincipalResolver = "how the server builds the user from the request"
