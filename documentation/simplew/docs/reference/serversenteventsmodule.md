# ServerSentEventsModule

The `ServerSentEventsModule` is used to set up Server-Sent Events.
This module sets up a middleware.


## Definition

```csharp
/// <summary>
/// Use Server Sent Events Module
/// It setups a Middleware
/// </summary>
public static SimpleWServer UseServerSentEventsModule(this SimpleWServer server, Action<ServerSentEventsOptions>? configure = null)
```


## Example

See [examples](../guide/serversentevents.md) of use.


## Authorization

Use `ServerSentEventsOptions.Authorize` to reject an SSE connection before the handshake:

```csharp
server.ConfigureChallenge(session => session.Response.Unauthorized().SendAsync());

server.UseServerSentEventsModule(options => {
    options.Prefix = "/events";
    options.Authorize = session => session.Principal.IsAuthenticated;
});
```

When `Authorize` returns `false`, the server-wide [`Challenge`](./simplewserver.md#configurechallenge) is invoked when configured and must send the response. Without it, the module responds with `403 Forbidden`. In both cases, the HTTP session is not switched to SSE streaming.
