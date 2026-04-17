# SimpleW.Service.Jwt

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Service.Jwt)](https://www.nuget.org/packages/SimpleW.Service.Jwt)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.Jwt)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

`SimpleW.Service.Jwt` is the convenience module built on top of `SimpleW.Helper.Jwt`.

It lets you:
- restore the principal automatically from the `Authorization` header
- protect handlers with `JwtAuthAttribute`
- enforce role checks with `RequireRoleAttribute`
- redirect anonymous requests to a login URL when configured
- bypass module authorization on public handlers with `AllowAnonymousAttribute`
- reuse a pre-built `JwtBearerHelper`

### Getting Started

Minimal module usage:

```cs
using SimpleW;
using SimpleW.Helper.Jwt;
using SimpleW.Service.Jwt;

namespace Sample {
    class Program {

        static async Task Main() {

            JwtBearerHelper jwt = new(options => {
                options.SecretKey = "super-secret-key";
                options.Issuer = "simplew";
                options.Audience = "api";
            });

            var server = new SimpleWServer(System.Net.IPAddress.Any, 2015);

            server.UseJwtModule(options => {
                options.Helper = jwt;
                options.LoginUrl = "/auth/login";
            });

            server.MapController<AccountController>("/api");

            await server.RunAsync();
        }
    }

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

}
```

With this configuration:
- authenticated requests restore `session.Principal` from the bearer token
- anonymous requests to `[JwtAuth]` handlers redirect to `LoginUrl`
- if `LoginUrl` is not configured, protected anonymous requests return a plain `401`
- role failures still return `403`

If you want to keep full control over the middleware policy, instantiate `JwtBearerHelper` directly from `SimpleW.Helper.Jwt` and wire the rules yourself.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
