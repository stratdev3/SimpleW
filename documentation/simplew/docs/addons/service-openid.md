# OpenID

The [`SimpleW.Service.OpenID`](https://www.nuget.org/packages/SimpleW.Service.OpenID) package provides a ready-to-use OpenID Connect module for SimpleW.

This package is built on top of [`SimpleW.Helper.OpenID`](./helper-openid.md) and adds a small policy layer:
- restore the principal from the OpenID auth cookie
- challenge decorated handlers automatically
- map `login`, `callback`, and `logout` routes for you

It is designed for applications that want OpenID with minimal wiring while keeping the actual OIDC protocol work inside the helper package.


## Features

It allows you to:
- register one or more OpenID providers
- keep the stateless helper behavior based on the provider `id_token`
- restore `session.Principal` automatically
- protect handlers with `[OpenIDAuth("provider")]`
- expose ready-to-use `GET` routes for login, callback, and logout
- reuse an existing `OpenIDHelper` when you already have one


## Requirements

- .NET 8.0
- SimpleW (core server)
- `SimpleW.Helper.OpenID` (automatically included)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Service.OpenID --version 26.0.0-rc.20260418-1789
```


## Configuration options

### UseOpenIDModule

```csharp
server.UseOpenIDModule(options => {
    options.Add("google", provider => {
        provider.Authority = "https://accounts.google.com";
        provider.ClientId = "<google-client-id>";
        provider.ClientSecret = "<google-client-secret>";
        provider.RedirectUri = "https://app.example.com/auth/oidc/callback/google";
    });
});
```

The module maps these routes under `BasePath`:
- `GET {BasePath}/login/:provider`
- `GET {BasePath}/callback/:provider`
- `GET {BasePath}/logout`

By default, `BasePath` is `/auth/oidc`.

### OpenIDModuleOptions

`OpenIDModuleOptions` extends `OpenIDHelperOptions`, so it inherits:
- `CookieName`
- `ChallengeCookieNamePrefix`
- `CookiePath`
- `CookieDomain`
- `CookieSecure`
- `CookieHttpOnly`
- `CookieSameSite`
- `SessionLifetime`
- `ChallengeLifetime`
- `BackchannelTimeout`
- `Backchannel`
- `AllowExternalReturnUrls`
- `CookieProtectionKey`
- `PrincipalFactory`
- `Add(...)` for providers

And adds the module-specific properties below:

| Option | Default | Description |
| ------ | ------- | ----------- |
| `BasePath` | `/auth/oidc` | Base path used for the technical OpenID routes. |
| `RestorePrincipal` | `true` | Restores `session.Principal` from the OpenID cookie before the handler runs. |
| `AutoChallenge` | `true` | Automatically redirects unauthenticated handlers decorated with `OpenIDAuthAttribute`. Requires `RestorePrincipal = true`. |
| `Helper` | `null` | Reuses an existing `OpenIDHelper` instead of creating one from inline options. |
| `ModulePrincipalFactory` | `null` | Optional module-level principal factory that receives route information in addition to the validated OpenID context. |

When `Helper` is provided, do not combine it with inline helper settings such as:
- provider registration through `Add(...)`
- cookie settings
- `PrincipalFactory`
- `ModulePrincipalFactory`

### Metadata attributes

| Attribute | Description |
| --------- | ----------- |
| `[OpenIDAuth("google")]` | Requires an authenticated OpenID principal for the named provider and redirects to the module login route when needed. |
| `[AllowAnonymous]` | Bypasses the module auto-challenge behavior for the decorated handler. |


## Minimal example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.OpenID;

var server = new SimpleWServer(IPAddress.Any, 8080);

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
            isAuthenticated = Principal.IsAuthenticated,
            name = Principal.Name,
            email = Principal.Email,
            provider = Principal.Get("provider")
        };
    }

}
```

With this configuration:
- the module restores the principal from the auth cookie
- `/api/account/me` redirects to the provider login flow when the user is anonymous
- `/auth/oidc/login/google` starts the challenge flow
- `/auth/oidc/callback/google` completes the login and redirects to `returnUrl`
- `/auth/oidc/logout` clears the local auth cookie and redirects to `returnUrl`


## Reusing a pre-built helper

If you already have a configured helper, inject it directly:

```csharp
using SimpleW.Helper.OpenID;
using SimpleW.Service.OpenID;

OpenIDHelper oidc = new(options => {
    options.CookieSecure = false;

    options.Add("google", provider => {
        provider.Authority = "https://accounts.google.com";
        provider.ClientId = "<google-client-id>";
        provider.ClientSecret = "<google-client-secret>";
        provider.RedirectUri = "http://127.0.0.1:8080/auth/oidc/callback/google";
    });
});

server.UseOpenIDModule(options => {
    options.BasePath = "/auth/oidc";
    options.Helper = oidc;
});
```

When `Helper` is provided, the module reuses it as-is.

If that helper uses a custom `PrincipalFactory`, keep a provider marker such as the `provider` identity property so `[OpenIDAuth("provider")]` can distinguish providers correctly.


## Principal Mapping (PrincipalFactory)

Because `OpenIDModuleOptions` inherits `OpenIDHelperOptions`, you can keep using the regular helper `PrincipalFactory`:

```csharp
server.UseOpenIDModule(options => {
    options.Add("google", provider => {
        provider.Authority = "https://accounts.google.com";
        provider.ClientId = "<google-client-id>";
        provider.ClientSecret = "<google-client-secret>";
        provider.RedirectUri = "https://app.example.com/auth/oidc/callback/google";
    });

    options.PrincipalFactory = context => {
        string subject = context.ClaimsPrincipal.FindFirst("sub")?.Value ?? "";
        string? email = context.ClaimsPrincipal.FindFirst("email")?.Value;

        return new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: $"OpenID:{context.ProviderName}",
            identifier: subject,
            name: email,
            email: email,
            roles: [ "user" ],
            properties: [
                new IdentityProperty("provider", context.ProviderName),
                new IdentityProperty("subject", subject)
            ]
        ));
    };
});
```

If you also need module route information such as `BasePath`, use `ModulePrincipalFactory` instead.


## When to use the helper package instead

Use [`SimpleW.Helper.OpenID`](./helper-openid.md) instead of this service package when you want:
- a custom middleware that decides auth policy differently
- technical routes fully controlled by the application
- advanced multi-auth strategies mixing OpenID with other schemes

The service package is intentionally thin.
It is a convenience wrapper around `OpenIDHelper`.
