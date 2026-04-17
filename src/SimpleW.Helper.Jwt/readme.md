# SimpleW.Helper.Jwt

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package provides lightweight JWT Bearer helpers for SimpleW.
It allows you to create and validate JWT tokens using `HttpIdentity` or `HttpPrincipal`, with full support for roles and custom properties.
Supports HS256, HS384, and HS512 algorithms, with optional issuer and audience validation.

### Getting Started

The minimal API

```cs
using System;
using System.Net;
using SimpleW;
using SimpleW.Helper.Jwt;

namespace Sample {
    class Program {

        static async Task Main() {

            var helper = new JwtBearerHelper(options => {
                options.SecretKey = "super-secret-key";
                options.Issuer = "simplew";
                options.Audience = "api";
            });

            var identity = new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "Custom",
                identifier: "user-123",
                name: "John Doe",
                email: "john@doe.com",
                roles: new[] { "admin" },
                properties: null
            );

            string token = helper.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            if (helper.TryValidateToken(token, out var principal, out var error)) {
                Console.WriteLine($"User: {principal.Identity.Name}");
            }
            else {
                Console.WriteLine($"Invalid token: {error}");
            }

        }
    }

}
```

### Middleware Integration

You can restore `session.Principal` from the `Authorization` header in a middleware.

```cs
using System;
using System.Collections.Generic;
using SimpleW;
using SimpleW.Helper.Jwt;

JwtBearerHelper helper = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

server.UseMiddleware(async (session, next) => {
    if (helper.TryAuthenticate(session, out HttpPrincipal principal)) {
        session.Principal = principal;
    }

    await next();
});
```

### Custom Attributes In Middleware

Because SimpleW exposes route metadata through `session.Metadata`, you can combine core auth metadata with custom JWT attributes in one middleware.

```cs
using SimpleW;
using SimpleW.Helper.Jwt;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class JwtAuthAttribute : Attribute, IHandlerMetadata {
}

JwtBearerHelper helper = new(options => {
    options.SecretKey = "super-secret-key";
    options.Issuer = "simplew";
    options.Audience = "api";
});

server.UseMiddleware(async (session, next) => {
    if (helper.TryAuthenticate(session, out HttpPrincipal principal)) {
        session.Principal = principal;
    }

    if (session.Metadata.Has<AllowAnonymousAttribute>()) {
        await next();
        return;
    }

    JwtAuthAttribute? auth = session.Metadata.Get<JwtAuthAttribute>();
    RequireRoleAttribute? requiredRole = session.Metadata.Get<RequireRoleAttribute>();

    if (auth == null && requiredRole == null) {
        await next();
        return;
    }

    if (!session.Principal.IsAuthenticated) {
        await session.Response.Unauthorized().SendAsync();
        return;
    }

    if (requiredRole != null && !session.Principal.IsInRoles(requiredRole.Role)) {
        await session.Response
                     .Status(403)
                     .Json(new { ok = false, error = "forbidden", role = requiredRole.Role })
                     .SendAsync();
        return;
    }

    await next();
});

[JwtAuth]
[Route("/api/account")]
public class AccountController : Controller {

    [AllowAnonymous]
    [Route("GET", "/public")]
    public object Public() {
        return new { ok = true };
    }

    [Route("GET", "/me")]
    public object Me() {
        return new {
            id = Principal.Identity.Identifier,
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

If you want a ready-to-use module that restores the principal automatically and protects handlers with metadata for you, use `SimpleW.Service.Jwt`.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
