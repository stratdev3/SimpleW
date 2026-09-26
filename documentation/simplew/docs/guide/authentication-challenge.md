# Authentication Challenge

An authentication challenge tells a client how to authenticate when a module explicitly requests it.

SimpleW keeps three responsibilities separate:

- authentication restores `session.Principal` from a bearer token, cookie, client certificate, or another trusted credential
- a module's `Authorize` callback decides whether to allow access, request authentication, or deny permission
- `SimpleWServer.Challenge` produces the response when that decision is `AuthorizeResult.Challenge`

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

Each module invokes it only when `Authorize` returns `AuthorizeResult.Challenge`. FileBrowser supports this for both `AccessModule` and concrete actions:

```csharp
server.ConfigurePrincipalResolver(ResolvePrincipal);

server.ConfigureChallenge(session =>
    session.Response.Unauthorized().SendAsync());

server.UseStaticFilesModule(options => {
    options.Path = "/srv/private";
    options.Prefix = "/private";
    options.Authorize = session => session.Principal.IsAuthenticated
                                        ? AuthorizeResult.Allowed
                                        : AuthorizeResult.Challenge;
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

The server challenge is not a global interceptor for every `401` or `403` response. It runs only when a participating module's `Authorize` callback returns `AuthorizeResult.Challenge`.

## Authorization Decisions

The public enum is defined in the `SimpleW` namespace:

```csharp
public enum AuthorizeResult {
    Forbidden = 0,
    Allowed = 1,
    Challenge = 2
}
```

- `Allowed` continues processing.
- `Challenge` invokes the configured server handler, or returns `403` when none is configured.
- `Forbidden` and unknown values return `403` without invoking the handler.

The callback decides explicitly; SimpleW does not infer the result from `Principal.IsAuthenticated`. An authenticated user may need another authentication step, or may simply lack permission.

This replaces the previous boolean callback API. Migrate `true` to `Allowed`, and choose `Challenge` or `Forbidden` for each former `false` according to its intent. StaticFiles, WebSocket and ServerSentEvents allow access when no callback is configured. FileBrowser defaults to a callback returning `Allowed` and rejects a null callback.
