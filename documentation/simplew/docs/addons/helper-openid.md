# OpenID

The [`SimpleW.Helper.OpenID`](https://www.nuget.org/packages/SimpleW.Helper.OpenID) package provides a lightweight OpenID Connect helper for SimpleW.


## Features

This package is intentionally focused on the OpenID Connect engine only:
- register multiple providers such as Google, Microsoft, Keycloak, or any OIDC authority
- create authorization challenge URLs
- complete callback requests
- validate `id_token` values
- create a `HttpPrincipal`
- authenticate a request from an auth cookie
- sign users out locally

It does **not** decide which routes must be protected.
That policy stays in your own custom middleware, which makes this package a good fit for:
- custom auth attributes based on `IHandlerMetadata`
- controller-specific authorization rules
- mixed authentication strategies chosen by the application


## Requirements

- .NET 8.0 or later
- SimpleW (core server)
- An OpenID Connect provider such as Google, Microsoft Entra ID, Keycloak, Auth0, or another OIDC authority


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.OpenID --version 26.0.0
```


## Flow Overview

| Step | Helper method | What happens |
| ---- | ------------- | ------------ |
| Authenticate request | `TryAuthenticate(session, out principal)` | Reads the auth cookie, validates the stored `id_token`, and rebuilds a fresh `HttpPrincipal`. |
| Start login | `CreateChallengeUrlAsync(session, provider, returnUrl, extraParameters)` | Creates a signed temporary challenge cookie and returns the provider authorization URL. |
| Finish login | `CompleteCallbackAsync(session, provider)` | Exchanges the callback `code`, validates the `id_token`, writes the auth cookie, and returns the principal. |
| Sign out | `SignOut(session, returnUrl)` | Deletes the local auth cookie and returns the normalized redirect URL. |

The helper is stateless for users: it does not keep authenticated sessions in server memory.
Already authenticated users survive a server restart while the provider `id_token` is still valid.


## Configuration Options

### OpenIDHelper

| Method | Description |
| ------ | ----------- |
| `TryAuthenticate(session, out principal)` | Reads the auth cookie, validates the stored OpenID `id_token`, and returns a fresh `HttpPrincipal`. |
| `CreateChallengeUrlAsync(session, provider, returnUrl, extraParameters)` | Creates the provider authorization URL. Redirect the response to this URL to start login. |
| `CompleteCallbackAsync(session, provider)` | Completes the callback flow, validates the ID token, creates the principal, and writes the auth cookie. |
| `SignOut(session, returnUrl)` | Deletes the auth cookie, then returns the redirect URL. |
| `HasProvider(provider)` | Returns `true` when the provider is configured. |

### OpenIDHelperOptions

| Option | Default | Description |
| ------ | ------- | ----------- |
| `CookieName` | `sw_oidc` | Auth cookie name. |
| `ChallengeCookieNamePrefix` | `sw_oidc_challenge_` | Prefix used for temporary challenge cookies. |
| `CookiePath` | `/` | Auth and challenge cookie path. |
| `CookieDomain` | `null` | Optional cookie domain. |
| `CookieSecure` | `true` | Writes cookies with the `Secure` flag. Set to `false` only for local HTTP development. |
| `CookieHttpOnly` | `true` | Writes cookies with the `HttpOnly` flag. |
| `CookieSameSite` | `Lax` | SameSite policy for auth and challenge cookies. |
| `SessionLifetime` | 8 hours | Maximum auth cookie lifetime. The cookie also expires no later than the provider `id_token`. |
| `ChallengeLifetime` | 10 minutes | Lifetime of the temporary `state`, `nonce`, and PKCE challenge. |
| `BackchannelTimeout` | 30 seconds | Timeout used when the helper creates its own HTTP client. |
| `Backchannel` | `null` | Optional custom HTTP client used for discovery and token requests. |
| `AllowExternalReturnUrls` | `false` | Keeps return URLs local by default to avoid open redirects. |
| `CookieProtectionKey` | random at startup | Optional key used only to sign temporary challenge cookies. Existing auth cookies survive restart while their `id_token` is valid. |
| `PrincipalFactory` | built-in | Maps validated OpenID claims to a `HttpPrincipal`. This is global and receives `context.ProviderName` for provider-specific mapping. |

### OpenIDProviderOptions

| Option | Default | Description |
| ------ | ------- | ----------- |
| `Authority` | empty | Provider authority, for example `https://accounts.google.com`. |
| `MetadataAddress` | authority metadata URL | Explicit `.well-known/openid-configuration` URL. |
| `ClientId` | empty | OpenID client id. |
| `ClientSecret` | `null` | OpenID client secret. |
| `RedirectUri` | empty | Callback URI registered at the provider. |
| `Scopes` | `openid profile email` | Requested scopes. `openid` is added automatically when missing. |
| `AuthorizationParameters` | empty | Extra authorization endpoint parameters such as `prompt` or `login_hint`. |
| `UsePkce` | `true` | Enables PKCE for the authorization code flow. |
| `ValidateNonce` | `true` | Validates the callback ID token nonce. |
| `RequireHttpsMetadata` | `true` | Requires HTTPS for provider metadata. |
| `ValidateIssuer` | `true` | Validates the token issuer. |
| `ValidIssuer` | metadata issuer | Optional issuer override. |
| `UseClientSecretBasicAuthentication` | `false` | Sends the client secret with HTTP Basic instead of form body. |
| `ClockSkew` | 5 minutes | Token lifetime clock skew. |
| `NameClaimType` | `name` | Claim used by `ClaimsPrincipal` for the name. |
| `RoleClaimType` | `role` | Claim used by `ClaimsPrincipal` for roles. |
| `RoleClaimTypes` | `role`, `roles`, `ClaimTypes.Role` | Claim types read by the default `HttpPrincipal` mapper. |
| `ConfigureTokenValidation` | `null` | Advanced hook to adjust token validation parameters. |

### OpenIDPrincipalContext

| Property | Description |
| -------- | ----------- |
| `Session` | Current `HttpSession`. |
| `ProviderName` | Logical provider name such as `google` or `microsoft`. |
| `Provider` | Provider configuration. |
| `ClaimsPrincipal` | Validated claims principal built from the `id_token`. |
| `AuthenticatedAt` | Authentication time in UTC. |

### OpenIDCallbackResult

| Property | Description |
| -------- | ----------- |
| `IsSuccess` | `true` when the callback completed successfully. |
| `StatusCode` | Suggested HTTP status code for failures. |
| `Error` | Error message when `IsSuccess` is `false`. |
| `Provider` | Logical provider name. |
| `ReturnUrl` | Normalized application return URL. |
| `Principal` | Authenticated principal when `IsSuccess` is `true`. |


## Minimal example

This example shows the intended architecture:
- an `OpenIDHelper` handles the OpenID Connect protocol
- a middleware authenticates the request from the OpenID auth cookie and decides whether the current controller action requires OpenID auth
- controllers declare intent through metadata attributes
- the application maps login, callback, and logout routes itself through controllers

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.OpenID;

namespace Sample {

    internal class Program {

        public static OpenIDHelper Oidc { get; private set; } = default!;

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 8080);

            Oidc = new OpenIDHelper(options => {
                options.CookieSecure = false; // local HTTP development only

                options.Add("google", provider => {
                    provider.Authority = "https://accounts.google.com";
                    provider.ClientId = "<google-client-id>";
                    provider.ClientSecret = "<google-client-secret>";
                    provider.RedirectUri = "http://127.0.0.1:8080/auth/openid/callback/google";
                    provider.Scopes = [ "openid", "profile", "email" ];
                });
            });

            // Custom auth middleware.
            server.UseMiddleware(async (session, next) => {

                // Authenticate from the OpenID auth cookie before policy middleware runs.
                if (Oidc.TryAuthenticate(session, out HttpPrincipal principal)) {
                    session.Principal = principal;
                }

                // fast path
                if (session.Metadata.Has<AllowAnonymousAttribute>()) {
                    await next().ConfigureAwait(false);
                    return;
                }

                // fast path : send challenge if not authenticated
                OpenIDAuthAttribute? auth = session.Metadata.Get<OpenIDAuthAttribute>();
                if (auth != null && !session.Principal.IsAuthenticated) {
                    string url = await Oidc.CreateChallengeUrlAsync(
                                    session,
                                    auth.Provider,
                                    returnUrl: session.Request.RawTarget
                                ).ConfigureAwait(false);

                    await session.Response.Redirect(url).SendAsync().ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });

            server.MapController<OpenIDController>("/auth");
            server.MapController<AccountController>("/api");

            await server.RunAsync();
        }

    }

    [Route("/openid")]
    public class OpenIDController : Controller {

        [AllowAnonymous]
        [Route("GET", "/login/:provider")]
        public async ValueTask<object> Login(string provider) {
            string url = await Program.Oidc.CreateChallengeUrlAsync(Session, provider).ConfigureAwait(false);
            return Response.Redirect(url);
        }

        [AllowAnonymous]
        [Route("GET", "/callback/:provider")]
        public async ValueTask<object> Callback(string provider) {
            OpenIDCallbackResult result = await Program.Oidc.CompleteCallbackAsync(Session, provider).ConfigureAwait(false);

            if (!result.IsSuccess) {
                return Response.Status(result.StatusCode).Json(new { ok = false, error = result.Error });
            }

            Session.Principal = result.Principal!;
            return Response.Redirect(result.ReturnUrl);
        }

        [AllowAnonymous]
        [Route("GET", "/logout")]
        public object Logout() {
            string returnUrl = Program.Oidc.SignOut(Session, "/");
            return Response.Redirect(returnUrl);
        }

    }

    [Route("/account")]
    public class AccountController : Controller {

        [AllowAnonymous]
        [Route("GET", "/public")]
        public object Public() {
            return new {
                login = "/auth/openid/login/google?returnUrl=/api/account/me",
                logout = "/auth/openid/logout?returnUrl=/"
            };
        }

        [OpenIDAuth("google")]
        [Route("GET", "/me")]
        public object Me() {
            return new {
                name = Principal.Name,
                email = Principal.Email,
                provider = Principal.Get("provider")
            };
        }

    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class OpenIDAuthAttribute : Attribute, IHandlerMetadata {

        public OpenIDAuthAttribute(string provider) {
            Provider = provider;
        }

        public string Provider { get; }

    }
}
```

In this model:
- the helper performs OpenID authentication
- the middleware decides whether the current controller action requires authentication
- the controller stays clean and only declares intent through metadata
- login, callback, and logout URLs remain under application control


## Multiple Providers

Register providers with stable logical names. Those names are later used in routes, metadata, and `context.ProviderName`.

```csharp
OpenIDHelper oidc = new(options => {
    options.Add("google", provider => {
        provider.Authority = "https://accounts.google.com";
        provider.ClientId = "<google-client-id>";
        provider.ClientSecret = "<google-client-secret>";
        provider.RedirectUri = "https://app.example.com/auth/openid/callback/google";
    });

    options.Add("microsoft", provider => {
        provider.Authority = "https://login.microsoftonline.com/<tenant-id>/v2.0";
        provider.ClientId = "<microsoft-client-id>";
        provider.ClientSecret = "<microsoft-client-secret>";
        provider.RedirectUri = "https://app.example.com/auth/openid/callback/microsoft";
        provider.Scopes = [ "openid", "profile", "email" ];
    });
});
```

You can then protect controller actions with the provider you expect:

```csharp
[Route("/admin")]
public class AdminController : Controller {

    [OpenIDAuth("microsoft")]
    [Route("GET", "/dashboard")]
    public object Dashboard() {
        return new {
            user = Principal.Name
        };
    }

}
```


## Custom principal mapping

You can fully control how validated OpenID claims become a `HttpPrincipal`.
There is one global `PrincipalFactory`. Use `context.ProviderName` when different providers need different mapping rules.

```csharp
OpenIDHelper oidc = new(options => {
    options.Add("google", provider => {
        provider.Authority = "https://accounts.google.com";
        provider.ClientId = "<google-client-id>";
        provider.ClientSecret = "<google-client-secret>";
        provider.RedirectUri = "https://app.example.com/auth/openid/callback/google";
    });

    options.PrincipalFactory = context => {
        string subject = context.ClaimsPrincipal.FindFirst("sub")?.Value ?? "";
        string? email = context.ClaimsPrincipal.FindFirst("email")?.Value;

        string[] roles = context.ProviderName switch {
            "google" => [ "user" ],
            "microsoft" => [ "employee" ],
            _ => []
        };

        return new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: $"OpenID:{context.ProviderName}",
            identifier: subject,
            name: email,
            email: email,
            roles: roles,
            properties: [
                new IdentityProperty("provider", context.ProviderName),
                new IdentityProperty("subject", subject)
            ]
        ));
    };
});
```


## Extra Authorization Parameters

Provider-specific authorization parameters can be configured globally on the provider:

```csharp
options.Add("google", provider => {
    provider.Authority = "https://accounts.google.com";
    provider.ClientId = "<google-client-id>";
    provider.ClientSecret = "<google-client-secret>";
    provider.RedirectUri = "https://app.example.com/auth/openid/callback/google";

    provider.AuthorizationParameters["prompt"] = "select_account";
});
```

You can also pass request-specific values when creating the challenge:

```csharp
string url = await oidc.CreateChallengeUrlAsync(
    session,
    "google",
    returnUrl: "/account",
    extraParameters: new Dictionary<string, string> {
        ["login_hint"] = "user@example.com"
    }
).ConfigureAwait(false);
```


## Integration Summary

| Step | Responsibility |
| ---- | -------------- |
| Register providers | `OpenIDHelper` |
| Create login challenge | `OpenIDHelper` |
| Complete callback | `OpenIDHelper` |
| Validate ID token | `OpenIDHelper` |
| Build `HttpPrincipal` | `OpenIDHelper` |
| Decide whether auth is required | your middleware |
| Declare route intent | your `IHandlerMetadata` attributes |
| Map technical routes | your application |


## Security Notes

- Always use HTTPS in production.
- Keep `CookieSecure = true` in production.
- Keep `RequireHttpsMetadata = true` in production.
- The helper is stateless: it stores no user session in server memory.
- The auth cookie stores the provider-issued OpenID `id_token`.
- Already authenticated users survive a server restart while the `id_token` is still valid.
- `TryAuthenticate()` validates that `id_token` on every request before rebuilding the `HttpPrincipal`.
- Challenge cookies are signed to protect `state`, `nonce`, PKCE, and `returnUrl`.
- If `CookieProtectionKey` is not configured, only in-progress login callbacks become invalid after process restart.
- `state`, `nonce`, and PKCE are generated and validated by the helper.
- Return URLs are local-only by default to avoid open redirects.
- Treat `PrincipalFactory` and `ConfigureTokenValidation` as trusted application code.


## When to use the service package instead

If you want a ready-to-use module that maps login, callback, logout, and automatic authentication behavior for you, use [`SimpleW.Service.OpenID`](./service-openid.md) instead.

That package is a thin module built on top of this helper.
