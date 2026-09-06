# Authentication Challenge

An authentication challenge tells a client how to continue when a module-wide authorization gate rejects a request.

SimpleW keeps three responsibilities separate:

- authentication restores `session.Principal` from a bearer token, cookie, client certificate, or another trusted credential
- a module's `Authorize` callback decides whether the principal may enter that module
- `SimpleWServer.Challenge` produces the response when that module-wide decision is `false`

The challenge is application-owned. It can send a `401`, add a `WWW-Authenticate` header, redirect to a login page, or start another authentication flow.

## Configure a Server-wide Challenge

Use [`ConfigureChallenge`](../reference/simplewserver.md#configurechallenge) once on the server:

```csharp
server.ConfigureChallenge(session =>
    session.Response
           .Unauthorized("Authentication required")
           .AddHeader("WWW-Authenticate", "Bearer")
           .SendAsync());
```

The configured callback is shared by authorization gates in:

- `FileBrowserModule`
- `StaticFilesModule`
- `ServerSentEventsModule`
- `WebSocketModule`

Each module invokes it only when its own `Authorize` callback returns `false`:

```csharp
server.ConfigurePrincipalResolver(ResolvePrincipal);

server.ConfigureChallenge(session =>
    session.Response.Unauthorized().SendAsync());

server.UseStaticFilesModule(options => {
    options.Path = "/srv/private";
    options.Prefix = "/private";
    options.Authorize = session => session.Principal.IsAuthenticated;
});
```

The callback must send the complete response. If no challenge is configured, the modules preserve their default `403 Forbidden` responses.

## Redirect to Login

For browser-facing modules, the challenge can preserve the requested URL and redirect to the application login route:

```csharp
server.ConfigureChallenge(session => {
    string returnUrl = string.IsNullOrWhiteSpace(session.Request.RawTarget)
                            ? session.Request.Path
                            : session.Request.RawTarget;

    string loginUrl = "/login?returnUrl=" + Uri.EscapeDataString(returnUrl);
    return session.Response.Redirect(loginUrl).SendAsync();
});
```

After authentication, the login endpoint can redirect the browser back to `returnUrl`.

::: warning
Validate that `returnUrl` is a local application path before redirecting to it. Do not accept an arbitrary absolute URL, or the login endpoint could become an open redirect.
:::


## Challenge Is Not Permission Denial

The server challenge is not a global interceptor for every `401` or `403` response. It runs only when a participating module's `Authorize` gate returns `false`.
