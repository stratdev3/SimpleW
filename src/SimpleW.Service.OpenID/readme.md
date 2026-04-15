# SimpleW.Service.OpenID

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Service.OpenID)](https://www.nuget.org/packages/SimpleW.Service.OpenID)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.OpenID)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

`SimpleW.Service.OpenID` is the convenience module built on top of `SimpleW.Helper.OpenID`.

It lets you:
- restore the principal automatically from the OpenID auth cookie
- challenge handlers decorated with `OpenIDAuthAttribute`
- map `login`, `callback`, and `logout` routes under a base path
- reuse the same provider and cookie options as `OpenIDHelper`

### Getting Started

Minimal module usage with controller metadata:

```cs
using SimpleW;
using SimpleW.Service.OpenID;

var server = new SimpleWServer(System.Net.IPAddress.Any, 8080);

server.UseOpenIDModule(options => {
    options.CookieSecure = false; // local HTTP development only

    options.Add("google", provider => {
        provider.Authority = "https://accounts.google.com";
        provider.ClientId = "<google-client-id>";
        provider.ClientSecret = "<google-client-secret>";
        provider.RedirectUri = "http://127.0.0.1:8080/auth/oidc/callback/google";
    });
});

server.MapController<AccountController>("/api");

await server.RunAsync();

[Route("/account")]
public class AccountController : Controller {

    [AllowAnonymous]
    [Route("GET", "/public")]
    public object Public() {
        return new {
            login = "/auth/oidc/login/google?returnUrl=/api/account/me",
            logout = "/auth/oidc/logout?returnUrl=/"
        };
    }

    [OpenIDAuth("google")]
    [Route("GET", "/me")]
    public object Me() {
        return new {
            user = Principal.Name,
            email = Principal.Email,
            provider = Principal.Get("provider")
        };
    }

}
```

Routes automatically mapped by the module:

- `GET /auth/oidc/login/:provider`
- `GET /auth/oidc/callback/:provider`
- `GET /auth/oidc/logout`

`OpenIDModuleOptions` inherits from `OpenIDHelperOptions`, so provider discovery, cookie behavior, token validation, and `PrincipalFactory` stay identical to the helper package.

Use this package when you want the OpenID plumbing and metadata-driven challenge behavior already wired.
Use `SimpleW.Helper.OpenID` directly when you want to keep full control over the middleware and technical routes.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
