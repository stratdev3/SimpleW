# SimpleW Migration Guide v26.1..v26.1.1

## Notes

SimpleW v26.1.1 introduces explicit authorization decisions for static files, Server-Sent Events and WebSockets. Their `Authorize` callbacks now return `AuthorizeResult` instead of `bool`, so applications using these callbacks must update their code even though this is a patch version.

The server-wide authentication challenge lets an application choose how clients authenticate. Per-response telemetry suppression is optional and requires no change to existing telemetry configuration.


## Quick reference

| v26.1 / earlier addon API | v26.1.1 / updated addon API |
|---|---|
| `Func<HttpSession, bool>` for module `Authorize` | `Func<HttpSession, AuthorizeResult>` |
| `return true` | `return AuthorizeResult.Allowed` |
| `return false` to deny access with `403` | `return AuthorizeResult.Forbidden` |
| Application-specific authentication response | `server.ConfigureChallenge(...)`, invoked by `AuthorizeResult.Challenge` |
| Server-wide telemetry configuration only | Keep `ConfigureTelemetry(...)`; optionally call `Response.DisableTelemetry()` for one exchange |


## Code breaking changes

### Module authorization callbacks

The `Authorize` option has changed in `StaticFilesModuleExtension.StaticFilesOptions`, `ServerSentEventsOptions` and `WebSocketOptions`.

Before:

```csharp
server.UseStaticFilesModule(options => {
    options.Path = "/srv/private";
    options.Prefix = "/private";
    options.Authorize = session => session.Principal.IsAuthenticated;
});
```

After:

```csharp
server.UseStaticFilesModule(options => {
    options.Path = "/srv/private";
    options.Prefix = "/private";
    options.Authorize = session => session.Principal.IsAuthenticated
        ? AuthorizeResult.Allowed
        : AuthorizeResult.Forbidden;
});
```

Apply the same replacement to `UseServerSentEventsModule(...)` and `UseWebSocketModule(...)`. Update named methods or stored delegates assigned to `Authorize` as well as inline lambdas. The enum is in the `SimpleW` namespace.

Returning `Forbidden` for a former `false` preserves the v26.1.0 denial response. Choose `Challenge` only when the application should start an authentication flow.

| Decision | Behavior |
|---|---|
| `Allowed` | Continue serving the file or accepting the SSE/WebSocket connection. |
| `Forbidden` | Return `403 Forbidden` without invoking the challenge. |
| `Challenge` | Invoke the server challenge if configured; otherwise return `403 Forbidden`. |
| Unknown enum value | Return `403 Forbidden`. |

These three core modules still allow access when `Authorize` is not configured. Rejected SSE and WebSocket requests remain HTTP responses; the connection is not upgraded. SimpleW does not choose a decision automatically from `Principal.IsAuthenticated`.

See the [StaticFiles](../reference/staticfilesmodule.md), [ServerSentEvents](../reference/serversenteventsmodule.md) and [WebSocket](../reference/websocketmodule.md) references.


## Authentication challenge

`SimpleWServer.ConfigureChallenge(HttpChallengeHandler)` registers the response to an explicit `AuthorizeResult.Challenge`. The callback returns `ValueTask` and must send the response.

For example, request a bearer token from anonymous clients and return `403` for authenticated users without the required role:

```csharp
server.ConfigureChallenge(session =>
    session.Response
           .Unauthorized("Authentication required")
           .AddHeader("WWW-Authenticate", "Bearer")
           .SendAsync());

server.UseStaticFilesModule(options => {
    options.Path = "/srv/private";
    options.Prefix = "/private";
    options.Authorize = session =>
        !session.Principal.IsAuthenticated
            ? AuthorizeResult.Challenge
            : session.Principal.IsInRole("file-reader")
                ? AuthorizeResult.Allowed
                : AuthorizeResult.Forbidden;
});
```

Keep your authentication layer or principal resolver: the challenge only sends a response and does not authenticate a client or populate `session.Principal`.

For browser-based authentication, the handler can send a redirect instead:

```csharp
server.ConfigureChallenge(session =>
    session.Response.Redirect("/auth/login").SendAsync());
```

The callback is shared by static files, SSE, WebSocket and FileBrowser authorization. It runs only for `Challenge`; it does not intercept arbitrary `401` or `403` responses returned by controllers, handlers or middleware. If you used an intermediate version that invoked the challenge for every `false` result, replace those results with `Challenge` where that behavior is intended.

See [Authentication Challenge](../guide/authentication-challenge.md) for login redirects and browser authentication flows.


## Optional per-response telemetry

`HttpResponse.DisableTelemetry()` disables SimpleW HTTP traces and request/response metrics for the current exchange while keeping logs. Existing applications do not need to change their telemetry setup.

For example, exclude a health endpoint before its handler runs:

```csharp
server.UseMiddleware(async (session, next) => {
    if (session.Request.Path == "/health") {
        session.Response.DisableTelemetry();
    }
    await next();
});

server.MapGet("/health", () => "OK");
```

Call the method before sending the response, as early as possible. Telemetry already emitted cannot be retracted. The flag resets for the next request on the connection, including keep-alive requests.

Continue to use `server.ConfigureTelemetry(...)` for server-wide settings. `Response.DisableTelemetry()` is a new response method; the former `server.DisableTelemetry()` removed in v26.1 remains unavailable. Application-created spans and addon-specific meters are outside this per-response setting.

See [Observability](../guide/observability.md#excluding-individual-requests) and [`HttpResponse.DisableTelemetry()`](../reference/httpresponse.md#disabletelemetry).

