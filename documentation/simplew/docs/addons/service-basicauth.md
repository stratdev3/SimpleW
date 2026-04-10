# BasicAuth

The [`SimpleW.Service.BasicAuth`](https://www.nuget.org/packages/SimpleW.Service.BasicAuth) package provides a ready-to-use Basic authentication module for SimpleW.

This package is built on top of [`SimpleW.Helper.BasicAuth`](./helper-basicauth.md) and adds a small policy layer:
- restore the principal from the `Authorization` header
- automatically challenge handlers decorated with `BasicAuthAttribute`
- bypass module authorization on `[AllowAnonymous]`
- optionally bypass protected `OPTIONS` requests for CORS preflight

It is designed for applications that want Basic auth with minimal wiring while keeping header parsing and credential validation inside the helper package.


## Features

It allows you to:
- restore `session.Principal` automatically from the Basic header
- protect controllers or handlers with `[BasicAuth("Admin Area")]`
- bypass module authorization on public handlers with `[AllowAnonymous]`
- configure users inline or reuse a pre-built `BasicAuthHelper`
- customize the created `HttpPrincipal`
- keep the module thin by delegating the auth engine to the helper package


## Requirements

- .NET 8.0
- SimpleW (core server)
- `SimpleW.Helper.BasicAuth` (automatically included)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Service.BasicAuth --version 26.0.0-rc.20260417-1773
```


## Configuration options

### UseBasicAuthModule

```csharp
server.UseBasicAuthModule(options => {
    options.Users = [
        new BasicUser("admin", "secret")
    ];
});
```

The module installs one middleware that can:
- restore the principal from the `Authorization` header
- challenge anonymous protected requests with the realm declared on `[BasicAuth(...)]`
- let `[AllowAnonymous]` handlers stay public
- let protected `OPTIONS` requests pass through when `BypassOptionsRequests = true`

Calling `UseBasicAuthModule(...)` again updates the current module configuration.

### BasicAuthModuleOptions

`BasicAuthModuleOptions` extends `BasicAuthOptions`, so it inherits:
- `Users`
- `CredentialValidator`
- `PrincipalFactory`

And adds the module-specific properties below:

| Option | Default | Description |
| ------ | ------- | ----------- |
| `RestorePrincipal` | `true` | Restores `session.Principal` from the `Authorization` header before the handler runs. |
| `AutoAuthorize` | `true` | Automatically protects handlers decorated with `BasicAuthAttribute`. Requires `RestorePrincipal = true`. |
| `BypassOptionsRequests` | `true` | Lets protected `OPTIONS` requests pass through without authentication. |
| `Helper` | `null` | Reuses an existing `BasicAuthHelper` instead of creating one from inline options. |
| `ModulePrincipalFactory` | `null` | Optional module-level principal factory that receives the current challenge realm in addition to the auth payload. |

When `Helper` is provided, do not combine it with inline helper settings such as:
- `Users`
- `CredentialValidator`
- `PrincipalFactory`
- `ModulePrincipalFactory`

### Metadata attributes

| Attribute | Description |
| --------- | ----------- |
| `[BasicAuth("Admin Area")]` | Requires an authenticated principal and uses the declared realm for the `WWW-Authenticate` challenge. |
| `[AllowAnonymous]` | Bypasses the module auto-authorization behavior for the decorated handler. |

`BasicAuthAttribute` is provided by `SimpleW.Service.BasicAuth`.

### BasicAuthModuleContext

`BasicAuthModuleContext` extends `BasicAuthContext` and adds:

| Property | Description |
| -------- | ----------- |
| `Realm` | Realm used by the current Basic challenge. |


## Minimal example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.BasicAuth;
using SimpleW.Service.BasicAuth;

var server = new SimpleWServer(IPAddress.Any, 2015);

server.UseBasicAuthModule(options => {
    options.Users = [
        new BasicUser("admin", "secret")
    ];
});

server.MapController<AdminController>("/api");

await server.RunAsync();

[Route("/admin")]
[BasicAuth("Admin Area")]
public class AdminController : Controller {

    [Route("GET", "/me")]
    public object Me() {
        return new {
            user = Principal.Name
        };
    }

    [AllowAnonymous]
    [Route("GET", "/health")]
    public object Health() {
        return new { ok = true };
    }

}
```

With this configuration:
- `/api/admin/me` requires Basic authentication
- `/api/admin/health` stays public thanks to `AllowAnonymous`
- unauthorized requests automatically receive a `401` Basic challenge with `realm="Admin Area"`
- authenticated requests have `session.Principal` and `Controller.Principal` populated


## Reusing a pre-built helper

If you already have a configured helper, you can inject it directly into the module:

```csharp
BasicAuthHelper basic = new(options => {
    options.Users = [
        new BasicUser("admin", "secret")
    ];
});

server.UseBasicAuthModule(options => {
    options.Helper = basic;
});
```

When `Helper` is provided, the module reuses it as-is.


## Custom principal mapping with module context

When you need the current challenge realm in your principal mapping, use `ModulePrincipalFactory`:

```csharp
server.UseBasicAuthModule(options => {
    options.Users = [
        new BasicUser("admin", "secret")
    ];

    options.ModulePrincipalFactory = context => {
        return new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: "Basic",
            identifier: context.Username,
            name: context.Username,
            email: null,
            roles: [ "admin" ],
            properties: [
                new IdentityProperty("basic_realm", context.Realm)
            ]
        ));
    };
});
```


## Bypass Options

By default, the module lets protected `OPTIONS` requests pass through:

```csharp
server.UseBasicAuthModule(options => {
    options.BypassOptionsRequests = true;
});
```

This is usually what you want for:
- browser CORS preflight requests
- API gateways
- reverse proxies

If you need strict Basic auth on every request, set `BypassOptionsRequests = false`.


## When to use the helper package instead

Use [`SimpleW.Helper.BasicAuth`](./helper-basicauth.md) instead of this service package when you want:
- a custom middleware that decides auth policy differently
- different credential sources depending on the current handler
- a middleware that mixes several authentication strategies

The service package is intentionally thin.
It is a convenience wrapper around metadata-driven Basic auth.
