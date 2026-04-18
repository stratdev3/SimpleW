# Jwt

The [`SimpleW.Helper.Jwt`](https://www.nuget.org/packages/SimpleW.Helper.Jwt) package provides a lightweight JWT Bearer helper for SimpleW.


## Features

This package is intentionally focused on the JWT engine only:
- create a JWT token from a `HttpIdentity` or `HttpPrincipal`
- validate a JWT token
- parse the `Authorization` header using the configured scheme
- create a `HttpPrincipal`

It does **not** decide which routes must be protected.
That policy stays in your own custom middleware, which makes this package a good fit for:
- custom auth attributes based on `IHandlerMetadata`
- controller-specific authorization rules
- mixed authentication strategies chosen by the application
- stateless APIs where the application owns token issuance


## Requirements

- .NET 8.0
- SimpleW (core server)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.Jwt --version 26.0.0
```


## Configuration options

### JwtBearerHelper

| Method | Description |
| ------ | ----------- |
| `CreateToken(principal, lifetime, issuer, audience, nowUtc)` | Creates a JWT token from a `HttpPrincipal`. |
| `CreateToken(identity, lifetime, issuer, audience, nowUtc)` | Creates a JWT token from a `HttpIdentity`. |
| `TryValidateToken(token, out principal, out error, nowUtc)` | Validates a JWT token string and rebuilds a `HttpPrincipal` when successful. |
| `TryAuthenticate(session, out principal)` | Parses the `Authorization` header, validates the JWT token, and returns a `HttpPrincipal` when authentication succeeds. |

### JwtBearerOptions

| Option | Default | Description |
| ------ | ------- | ----------- |
| `SecretKey` | empty | Shared secret used to sign and validate HMAC JWT tokens. |
| `Issuer` | `null` | Issuer written to created tokens and validated on read. Alias of `ExpectedIssuer`. |
| `Audience` | `null` | Audience written to created tokens and validated on read. Alias of `ExpectedAudience`. |
| `ExpectedIssuer` | `null` | Expected `iss` claim. If set, the token must match this value. |
| `ExpectedAudience` | `null` | Expected `aud` claim. If set, the token must match this value. |
| `ClockSkew` | `1 minute` | Allowed clock drift when validating `exp` and `nbf`. |
| `Algorithm` | `HS256` | HMAC algorithm used to sign tokens (`HS256`, `HS384`, `HS512`). |
| `Scheme` | `Bearer` | Authorization scheme parsed from the request. |
| `AuthenticationType` | `Bearer` | Authentication type assigned to the rebuilt `HttpIdentity`. |
| `PrincipalFactory` | built-in | Maps a validated JWT token to a `HttpPrincipal`. |

### JwtPrincipalContext

| Property | Description |
| -------- | ----------- |
| `Session` | Current `HttpSession` when authentication comes from `TryAuthenticate`. Otherwise `null`. |
| `Token` | Validated raw JWT token string. |
| `Subject` | JWT `sub` claim. |
| `Name` | JWT `name` claim. |
| `Email` | JWT `email` claim. |
| `Issuer` | JWT `iss` claim. |
| `Audiences` | JWT `aud` claim values. |
| `Roles` | Roles extracted from `role` or `roles`. |
| `Properties` | Remaining JWT claims mapped to `IdentityProperty`. |
| `AuthenticatedAt` | UTC timestamp used while rebuilding the principal. |
| `AuthenticationType` | Authentication type selected by the helper options. |
| `Scheme` | Authorization scheme used by the helper. |


## Minimal Example

This example shows the intended architecture:
- a `JwtBearerHelper` handles JWT creation and validation
- a custom middleware reads handler metadata from `session.Metadata`
- controllers decide which endpoints require authentication

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using SimpleW;
using SimpleW.Helper.Jwt;

SimpleWServer server = new(IPAddress.Any, 2015);

JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

server.UseMiddleware(async (session, next) => {

    // restore the jwt principal when a Bearer token is present
    if (jwt.TryAuthenticate(session, out HttpPrincipal principal)) {
        session.Principal = principal;
    }

    // fast path
    if (session.Metadata.Has<AllowAnonymousAttribute>()) {
        await next().ConfigureAwait(false);
        return;
    }

    JwtAuthAttribute? auth = session.Metadata.Get<JwtAuthAttribute>();
    RequireRoleAttribute? requiredRole = session.Metadata.Get<RequireRoleAttribute>();

    if (auth == null && requiredRole == null) {
        await next().ConfigureAwait(false);
        return;
    }

    if (!session.Principal.IsAuthenticated) {
        await session.Response.Unauthorized().SendAsync().ConfigureAwait(false);
        return;
    }

    if (requiredRole != null && !session.Principal.IsInRoles(requiredRole.Role)) {
        await session.Response
                     .Status(403)
                     .Json(new {
                         ok = false,
                         error = "forbidden",
                         role = requiredRole.Role
                     })
                     .SendAsync()
                     .ConfigureAwait(false);
        return;
    }

    await next().ConfigureAwait(false);
});

server.MapController<AccountController>("/api");

await server.RunAsync();

[Route("/account")]
[JwtAuth]
public class AccountController : Controller {

    [Route("GET", "/me")]
    public object Me() {
        return new {
            id = Principal.Identity.Identifier,
            name = Principal.Name,
            roles = Principal.Roles
        };
    }

    [AllowAnonymous]
    [Route("GET", "/public")]
    public object Public() {
        return new { ok = true };
    }

    [RequireRole("admin")]
    [Route("GET", "/admin")]
    public object Admin() {
        return new { ok = true, area = "admin" };
    }

}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class JwtAuthAttribute : Attribute, IHandlerMetadata {
}

```

In this model:
- the helper performs JWT parsing and validation
- the middleware decides whether the current handler requires authentication thanks to custom attribute
- the controller stays clean and only declares intent through metadata


## Creating tokens

The helper can create a token from either a `HttpIdentity` or a `HttpPrincipal`.

```csharp
JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

HttpIdentity identity = new(
    isAuthenticated: true,
    authenticationType: "Bearer",
    identifier: "user-1",
    name: "John",
    email: "john@doe.com",
    roles: [ "admin" ],
    properties: [
        new IdentityProperty("tenant_id", "acme")
    ]
);

string token = jwt.CreateToken(
    identity,
    lifetime: TimeSpan.FromHours(1)
);
```

Core claims are generated automatically from the identity:
- `sub`
- `name`
- `email`
- `role` or `roles`
- `iat`
- `nbf`
- `exp`
- optionally `iss` and `aud`


## Direct token validation

You can also validate tokens outside of HTTP middleware:

```csharp
JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

if (jwt.TryValidateToken(token, out HttpPrincipal? principal, out string? error)) {
    Console.WriteLine(principal?.Name);
}
else {
    Console.WriteLine(error);
}
```

This is useful when you:
- validate a token received outside the `Authorization` header
- need JWT verification in background jobs or other application services
- want deterministic tests by passing `nowUtc`


## Custom principal mapping

You can fully control how a validated JWT becomes a `HttpPrincipal`:

```csharp
JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";

    options.PrincipalFactory = context => {
        return new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: "Jwt",
            identifier: context.Subject,
            name: context.Name,
            email: context.Email,
            roles: context.Roles,
            properties: [
                new IdentityProperty("login", context.Name ?? context.Subject ?? "unknown"),
                new IdentityProperty("source", "jwt"),
                new IdentityProperty("issuer", context.Issuer ?? string.Empty)
            ]
        ));
    };
});
```

This is useful when you want to:
- map custom claims to application roles
- enforce your own identity shape
- add domain-specific properties to the principal


## Custom scheme

You can customize the parsed scheme and the rebuilt authentication type:

```csharp
JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Scheme = "Token";
    options.AuthenticationType = "ApiToken";
});
```

In this case:
- `TryAuthenticate()` expects `Authorization: Token ...`
- created principals use `ApiToken` as `AuthenticationType`


## Integration Summary

| Step | Responsibility |
| ---- | -------------- |
| Create JWT token | `JwtBearerHelper` |
| Parse `Authorization` header | `JwtBearerHelper` |
| Validate JWT token | `JwtBearerHelper` |
| Build `HttpPrincipal` | `JwtBearerHelper` |
| Decide whether auth is required | your middleware |
| Declare route intent | your `IHandlerMetadata` attributes |


## Security Notes

- Only HMAC (symmetric) algorithms are supported
- Tokens are signed, not encrypted
- Keep your `SecretKey` private and strong
- Always validate `iss`, `aud`, `exp`, and `nbf` when relevant for your application
- Treat `PrincipalFactory` as trusted application code
- Use HTTPS in production when tokens travel through HTTP headers


## When to use the service package instead

If you want a ready-to-use module that restores the principal automatically and protects handlers with metadata such as `[JwtAuth]` and `[RequireRole(...)]`, use [`SimpleW.Service.Jwt`](./service-jwt.md) instead.

That package is a thin module built on top of this helper.
