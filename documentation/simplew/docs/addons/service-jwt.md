# Jwt

The [`SimpleW.Service.Jwt`](https://www.nuget.org/packages/SimpleW.Service.Jwt) package provides a ready-to-use JWT Bearer module for SimpleW.

This package is built on top of [`SimpleW.Helper.Jwt`](./helper-jwt.md) and adds a small policy layer:
- restore the principal from the `Authorization` header
- optionally redirect anonymous requests to a login URL
- enforce role requirements through metadata

It is designed for applications that want JWT protection with minimal wiring while keeping token parsing and validation inside the helper package.


## Features

It allows you to:
- restore `session.Principal` automatically from the configured bearer scheme
- protect handlers with `[JwtAuth]`
- redirect anonymous protected requests to `LoginUrl` when configured
- bypass module authorization on public handlers with `[AllowAnonymous]`
- enforce role requirements with `[RequireRole("admin")]`
- reuse an existing `JwtBearerHelper` when you already have one


## Requirements

- .NET 8.0
- SimpleW (core server)
- `SimpleW.Helper.Jwt` (automatically included)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Service.Jwt --version 26.0.0-rc.20260418-1789
```


## Configuration options

### UseJwtModule

```csharp
server.UseJwtModule(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
    options.LoginUrl = "/auth/login";
});
```

The module installs one middleware that can:
- restore the principal from the `Authorization` header
- redirect anonymous protected requests to `LoginUrl`
- return a plain `401` when `LoginUrl` is not configured
- return `403 Forbidden` for unmet `[RequireRole(...)]` requirements

### JwtModuleOptions

`JwtModuleOptions` extends `JwtBearerOptions`, so it inherits:
- `SecretKey`
- `Issuer`
- `Audience`
- `ExpectedIssuer`
- `ExpectedAudience`
- `ClockSkew`
- `Algorithm`
- `Scheme`
- `AuthenticationType`
- `PrincipalFactory`

And adds the module-specific properties below:

| Option | Default | Description |
| ------ | ------- | ----------- |
| `LoginUrl` | `null` | Optional login URL used for anonymous requests that target protected handlers. When null, the module returns a plain `401`. |
| `RestorePrincipal` | `true` | Restores `session.Principal` from the `Authorization` header before the handler runs. |
| `AutoAuthorize` | `true` | Automatically protects handlers decorated with `JwtAuthAttribute` or `RequireRoleAttribute`. Requires `RestorePrincipal = true`. |
| `ReturnUrlParameterName` | `returnUrl` | Query-string parameter appended to `LoginUrl` with the current request path. Set to `null` or empty to disable it. |
| `Helper` | `null` | Reuses an existing `JwtBearerHelper` instead of creating one from inline options. |
| `ModulePrincipalFactory` | `null` | Optional module-level principal factory that receives the validated JWT payload plus the configured login settings. |

When `Helper` is provided, do not combine it with inline helper settings such as:
- `SecretKey`
- issuer / audience settings
- `ClockSkew`
- `Algorithm`
- `Scheme`
- `AuthenticationType`
- `PrincipalFactory`
- `ModulePrincipalFactory`

### Metadata attributes

| Attribute | Description |
| --------- | ----------- |
| `[JwtAuth]` | Requires an authenticated principal and triggers a redirect to `LoginUrl` when configured, otherwise a plain `401`. |
| `[RequireRole("admin")]` | Requires at least one of the roles described by the attribute `Role` expression. Multiple attributes can be combined. |
| `[AllowAnonymous]` | Bypasses the module auto-authorization behavior for the decorated handler. |


## Minimal example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.Jwt;
using SimpleW.Service.Jwt;

JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseJwtModule(options => {
    options.Helper = jwt;
    options.LoginUrl = "/auth/login";
});

server.MapController<AccountController>("/api");

await server.RunAsync();

[Route("/account")]
[JwtAuth]
public class AccountController : Controller {

    [AllowAnonymous]
    [Route("GET", "/public")]
    public object Public() {
        return new { ok = true };
    }

    [Route("GET", "/me")]
    public object Me() {
        return new {
            isAuthenticated = Principal.IsAuthenticated,
            name = Principal.Name,
            roles = Principal.Roles
        };
    }

    [RequireRole("admin")]
    [Route("GET", "/admin")]
    public object Admin() {
        return new { ok = true, area = "admin" };
    }

}
```

With this configuration:
- the module restores the principal from the bearer token when present
- `/api/account/me` redirects to `/auth/login?returnUrl=%2Fapi%2Faccount%2Fme` when the user is anonymous
- if `LoginUrl` is not configured, protected anonymous requests return a plain `401`
- `/api/account/admin` returns `403` when the authenticated principal does not satisfy the role requirement
- `/api/account/public` stays public thanks to `AllowAnonymous`


## Reusing a pre-built helper

If you already have a configured helper, inject it directly:

```csharp
using SimpleW.Helper.Jwt;
using SimpleW.Service.Jwt;

JwtBearerHelper jwt = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

server.UseJwtModule(options => {
    options.Helper = jwt;
    options.LoginUrl = "/auth/login";
});
```

When `Helper` is provided, the module reuses it as-is.


## Principal Mapping (PrincipalFactory)

Because `JwtModuleOptions` inherits `JwtBearerOptions`, you can keep using the regular helper `PrincipalFactory`:

```csharp
server.UseJwtModule(options => {
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
                new IdentityProperty("issuer", context.Issuer ?? string.Empty),
                new IdentityProperty("source", "jwt-helper")
            ]
        ));
    };
});
```

If you also need the configured `LoginUrl` or `ReturnUrlParameterName`, use `ModulePrincipalFactory` instead.


## When to use the helper package instead

Use [`SimpleW.Helper.Jwt`](./helper-jwt.md) instead of this service package when you want:
- a custom middleware that decides auth policy differently
- mixed authentication strategies with more advanced rules
- token validation outside HTTP middleware or background services

The service package is intentionally thin.
It is a convenience wrapper around `JwtBearerHelper`.
