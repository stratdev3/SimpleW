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

This package lightweight JWT Bearer helpers for SimpleW.
It allows you to easily create and validate JWT tokens using HttpIdentity or HttpPrincipal, with full support for roles and custom properties.
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

            // create options
            var options = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "simplew",
                audience: "api"
            );

            // create an identity
            var identity = new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "Custom",
                identifier: "user-123",
                name: "John Doe",
                email: "john@doe.com",
                roles: new[] { "admin" }
            );

            // create token
            string token = JwtBearerHelper.CreateToken(
                options,
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            // validate token
            if (JwtBearerHelper.TryValidateToken(options, token, out var principal, out var error)) {
                Console.WriteLine($"User: {principal.Identity.Name}");
            }
            else {
                Console.WriteLine($"Invalid token: {error}");
            }

        }
    }

}
```

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.