# BasicAuth

The [`SimpleW.Helper.BasicAuth`](https://www.nuget.org/packages/SimpleW.Helper.BasicAuth) package provides a lightweight HTTP Basic authentication helper for SimpleW.


## Features

This package is intentionally focused on the authentication engine only:
- parse the `Authorization` header
- validate a username/password pair
- create a `HttpPrincipal`
- send a `401` Basic challenge

It does **not** decide which routes must be protected.
That policy stays in your own custom middleware, which makes this package a good fit for:
- custom auth attributes based on `IHandlerMetadata`
- controller-specific authorization rules
- mixed authentication strategies chosen by the application


## Requirements

- .NET 8.0
- SimpleW (core server)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.BasicAuth --version 26.0.0
```


## Configuration options

### BasicAuthHelper

| Method | Description |
| ------ | ----------- |
| `TryAuthenticate(session, out principal)` | Parses the `Authorization` header, validates credentials, and returns a `HttpPrincipal` when authentication succeeds. |
| `SendChallengeAsync(session, realm)` | Sends a `401 Unauthorized` response with the `WWW-Authenticate` header for Basic auth. |

### BasicAuthOptions

| Option | Default | Description |
| ------ | ------- | ----------- |
| `Users` | empty | Static username/password list used when no custom validator is provided. |
| `CredentialValidator` | `null` | Optional callback used to validate username/password pairs yourself. |
| `PrincipalFactory` | built-in | Maps a successful authentication to a `HttpPrincipal`. |

### BasicAuthContext

| Property | Description |
| -------- | ----------- |
| `Session` | Current `HttpSession`. |
| `Username` | Username extracted from the `Authorization` header. |
| `Password` | Password extracted from the `Authorization` header. |

### Example metadata attribute

| Property | Description |
| -------- | ----------- |
| `Realm` | Realm used by your middleware when it sends a Basic challenge for the current handler. |

The helper package does not provide a handler metadata attribute.
In a helper-only integration, you define your own `BasicAuthAttribute` in the application.


## Minimal example

This example shows the intended architecture:
- a `BasicAuthHelper` handles the Basic auth protocol
- a custom middleware reads handler metadata from `session.Metadata`
- controllers decide which endpoints require authentication

```csharp
using System;
using System.Net;
using SimpleW;
using SimpleW.Helper.BasicAuth;

var server = new SimpleWServer(IPAddress.Any, 2015);

// configure basic helper
BasicAuthHelper basic = new(options => {
    options.Users = [
        new BasicUser("admin", "secret")
    ];
});

// use the basic helper in a custom auth middleware
server.UseMiddleware(async (session, next) => {

    // restore the basicauth principal
    if (basic.TryAuthenticate(session, out HttpPrincipal principal)) {
        session.Principal = principal;
    }

    // fast path
    if (session.Metadata.Has<AllowAnonymousAttribute>()) {
        await next().ConfigureAwait(false);
        return;
    }

    // check principal
    BasicAuthAttribute? auth = session.Metadata.Get<BasicAuthAttribute>();
    if (auth != null && !session.Principal.IsAuthenticated) {
        await basic.SendChallengeAsync(session, auth.Realm).ConfigureAwait(false);
        return;
    }

    await next().ConfigureAwait(false);
});

server.MapController<AdminController>("/api");
server.MapController<HomeController>("/api");

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

[Route("/home")]
public class HomeController : Controller {

    [BasicAuth("Home Area")]
    [Route("GET", "/me")]
    public object Me() {
        return new {
            user = Principal.Name
        };
    }

    [Route("GET", "/index")]
    public object Index() {
        return new { ok = true };
    }

}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class BasicAuthAttribute : Attribute, IHandlerMetadata {

    public BasicAuthAttribute(string realm = "Restricted") {
        Realm = realm;
    }

    public string Realm { get; }

}
```

In this model:
- the helper performs authentication
- the middleware decides whether the current handler requires authentication thanks to `BasicAuthAttribute`
- the controller stays clean and only declares intent through metadata


## Custom credential validation

Instead of a static users list, you can validate credentials yourself:

```csharp
BasicAuthHelper basic = new(options => {
    options.CredentialValidator = (username, password) => {
        return username == "admin" && password == "secret";
    };
});
```

This is useful when credentials come from:
- a database
- a configuration provider
- an external service


## Custom principal mapping

You can fully control how an authenticated user becomes a `HttpPrincipal`:

```csharp
BasicAuthHelper basic = new(options => {
    options.Users = [
        new BasicUser("admin", "secret")
    ];

    options.PrincipalFactory = context => {
        return new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: "Basic",
            identifier: context.Username,
            name: context.Username,
            email: null,
            roles: [ "admin" ],
            properties: [
                new IdentityProperty("login", context.Username),
                new IdentityProperty("source", "basic-auth")
            ]
        ));
    };
});
```


## Integration Summary

| Step | Responsibility |
| ---- | -------------- |
| Parse Basic auth header | `BasicAuthHelper` |
| Validate credentials | `BasicAuthHelper` |
| Build `HttpPrincipal` | `BasicAuthHelper` |
| Decide whether auth is required | your middleware |
| Declare route intent | your `IHandlerMetadata` attributes |


## Security Notes

- HTTP Basic credentials are only base64-encoded, not encrypted
- Always use HTTPS in production
- Keep realms explicit so browser prompts stay understandable
- Treat `CredentialValidator` and `PrincipalFactory` as trusted application code


## When to use the service package instead

Use [`SimpleW.Service.BasicAuth`](./service-basicauth.md) instead of this helper package when you want:
- a ready-to-use module that restores `session.Principal` automatically
- automatic enforcement of `[BasicAuth(...)]`
- built-in support for `[AllowAnonymous]` and protected `OPTIONS` bypass

That package is a thin module built on top of this helper and provides its own `BasicAuthAttribute`.
