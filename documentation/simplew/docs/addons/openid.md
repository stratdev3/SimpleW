# OpenID

[`SimpleW.Service.OpenID`](https://www.nuget.org/packages/SimpleW.Service.OpenID) is an OpenID Connect (OIDC) authentication module for the SimpleW web server.

It allows SimpleW applications to authenticate users using external identity providers such as **Google, Apple, Azure AD**, or **Keycloak**, using the standard **Authorization Code flow**.

This module is **not based on ASP.NET**, does not rely on any hidden framework behavior, and integrates directly with SimpleW routing, sessions, and user model.


## What this package does

`SimpleW.Service.OpenID` allows you to :
- Authenticate users via OpenID Connect (OIDC)
- Use external identity providers (Google, Apple, Azure AD, Keycloak, etc.)
- Handle the full Authorization Code flow
- Validate ID tokens (issuer, audience, lifetime, signature, nonce)
- Create secure, cookie-based authentication sessions
- Automatically restore authenticated users on each request
- Map OpenID claims to SimpleW users (`IWebUser`, `WebUser`, `TokenWebUser`)
- Support multiple identity providers
- Keep full control over routing, cookies, and sessions


## Installation

```sh
$ dotnet add package SimpleW.Service.OpenID --version 26.0.0-beta.20260215-1450
```


## Basic Usage

### Minimal Example

```csharp
server.UseOpenIDModule(options => {
    options.Add("google", o => {
        o.Authority = "https://accounts.google.com";
        o.ClientId = "<client-id>";
        o.ClientSecret = "<client-secret>";
        o.PublicBaseUrl = "https://myapp.example.com";
    });

    options.DefaultProvider = "google";
});
```

This configuration enables :
- `/auth/oidc/login/google`
- `/auth/oidc/callback/google`
- `/auth/oidc/logout`


### Multi-provider usage

You can configure multiple identity providers at the same time :

```csharp
server.UseOpenIDModule(options => {
    options.Add("google", o => {
        o.Authority = "https://accounts.google.com";
        o.ClientId = "<google-client-id>";
        o.ClientSecret = "<google-client-secret>";
        o.PublicBaseUrl = "https://myapp.example.com";
    });
    options.Add("apple", o => {
        o.Authority = "https://appleid.apple.com";
        o.ClientId = "<apple-client-id>";
        o.ClientSecret = "<apple-client-secret>";
        o.PublicBaseUrl = "https://myapp.example.com";
    });
});
```

Each provider is accessed through :

```
/auth/oidc/login/{provider}
/auth/oidc/callback/{provider}
```

Example :

```
/auth/oidc/login/google
/auth/oidc/login/apple
```


## Login flow

1. User accesses `/auth/oidc/login/:provider`
2. User is redirected to the identity provider
3. Provider redirects back to `/auth/oidc/callback/:provider`
4. Authorization code is exchanged for tokens
5. ID token is validated (OIDC rules)
6. A secure authentication cookie is created
7. User is redirected back to the original page


## User session handling

After authentication :

- A secure cookie is stored on the client
- Session data is stored in memory on the server
- On each request, the module:
  - Reads the cookie
  - Restores the session
  - Injects the authenticated user into `HttpSession.Request.User`

```csharp
if (session.Request.User.Identity) {
    // user is authenticated
}
```


## Accessing the authenticated user

Example API endpoint :

```csharp
server.Router.MapGet("/api/me", (HttpSession session) => {
    if (!session.Request.User.Identity) {
        return session.Response
                      .Unauthorized()
                      .SendAsync();
    }

    return session.Response.Json(new {
        session.Request.User.Id,
        session.Request.User.Login,
        session.Request.User.Mail,
        session.Request.User.FullName,
        session.Request.User.Roles
    }).SendAsync();
});
```


## User mapping (`UserFactory`)

By default, OpenID claims are mapped to `WebUser` or `TokenWebUser`.

You can fully customize this behavior :

```csharp
options.Add("google", o => {
    o.UserFactory = auth => {
        var user = new WebUser {
            Identity = true,
            Login = auth.Claims.FirstOrDefault(c => c.Type == "email")?.Value
        };

        return user;
    };
});
```


## Security considerations

- State and nonce are generated for each login attempt
- State values have a configurable TTL
- ID tokens are fully validated
- Cookies support Secure and SameSite settings
- Open redirects are explicitly prevented


## Configuration options

### OpenIDMultiOptions (global / multi-provider)

| Option | Default | Description |
|------|---------|-------------|
| BasePath | `/auth/oidc` | Base path for all OpenID routes (`/login/{provider}`, `/callback/{provider}`, `/logout`). |
| DefaultProvider | `null` | Default provider used when calling `/auth/oidc/login` without specifying a provider. |
| CookieName | `simplew_oidc` | Name of the authentication cookie shared across all providers. |
| CookieSecure | `true` | Marks the authentication cookie as Secure (HTTPS only). |
| CookieSameSite | `Lax` | SameSite policy for the authentication cookie. |
| Add(name, configure) | — | Registers a new OpenID provider with its own OpenIDOptions. |

### OpenIDOptions (per provider)

| Option | Default | Description |
|------|---------|-------------|
| Authority* | — | OpenID provider base URL (e.g. `https://accounts.google.com`). |
| ClientId* | — | OAuth2 / OpenID client identifier. |
| ClientSecret* | — | Client secret used to authenticate against the token endpoint. |
| PublicBaseUrl* | — | Public base URL of the application (used to build redirect URIs). |
| RedirectUri | `null` | Explicit redirect URI. Overrides PublicBaseUrl if set. |
| Scopes | `openid profile email` | Scopes requested during authentication. |
| RequireHttpsMetadata | `true` | Requires HTTPS when retrieving OIDC discovery metadata. |
| StateTtlMinutes | `10` | Lifetime of the temporary authentication state (state + nonce). |
| SessionTtlMinutes | `480` | Default session lifetime when the provider does not specify one. |
| ClockSkewSeconds | `60` | Allowed clock skew when validating token timestamps. |
| CleanupEveryNRequests | `256` | Number of requests between cleanup passes (no background timers). |
| CookieName | inherited | Cookie name (usually inherited from OpenIDMultiOptions). |
| CookieSecure | inherited | Marks the authentication cookie as Secure. |
| CookieSameSite | inherited | SameSite policy for the authentication cookie. |
| SaveTokens | `false` | Stores provider tokens (id_token, access_token) in memory. |
| Prompt | `null` | Optional OIDC prompt parameter (e.g. `select_account`). |
| LoginHint | `null` | Enables support for the login_hint query parameter. |
| ClientAuthentication | `Basic` | Client authentication method (Basic or PostBody). |
| HttpClient | `null` | Custom HttpClient used for OIDC requests. |
| UserFactory | default | Maps an OpenID session to a SimpleW user instance. |

### ClientAuthMode

| Value | Description |
|------|-------------|
| Basic | Uses HTTP Basic authentication (`Authorization: Basic base64(client_id:client_secret)`). |
| PostBody | Sends client_secret in the POST body (supported by some providers). |


## Limitations

- Sessions are stored in memory (not distributed)
- No refresh token rotation by default
- Facebook is not a native OIDC provider (use Keycloak or Auth0)
- Apple requires a JWT-based client secret
