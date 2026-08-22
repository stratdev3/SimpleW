# Server

[`SimpleWServer`](../reference/simplewserver.md) is the main entry point to host a SimpleW Web server.
You can use it in a **procedural** style (step-by-step calls) or in a **fluent** style (chaining calls).


## Lifecycle actions

| Method | Behavior |
|---|---|
| `StartAsync(ct)` | Starts listening and completes once startup succeeds, without waiting for the server lifetime. |
| `RunAsync(ct)` | Starts the server and **blocks** until the server is stopped / cancelled. |
| `StopAsync()` | Stops listening, closes sessions, disposes internal resources. |
| `ReloadListenerAsync(reconfigure, ct)` | Hot-reloads the **listener** (endpoint / TLS, etc.) while the server is running. |


## Lifecycle state

`SimpleWServer.State` is a thread-safe snapshot represented by `SimpleWServerState`:

| State | Meaning |
|---|---|
| `Stopped` | Not running; pre-start configuration is allowed. |
| `Starting` | The engine is starting. |
| `Started` | Running normally. |
| `Reloading` | Replacing or restoring the listener. |
| `Stopping` | Stopping the engine and cleaning local resources. |
| `Faulted` | Startup cleanup, listener restoration, or engine shutdown failed. |

The normal transitions are:

```text
Stopped -> Starting -> Started
Started -> Reloading -> Started
Started -> Stopping -> Stopped
```

All lifecycle operations use the same synchronization point. Concurrent calls wait for the transition in progress and then apply their intent. For example, two starts only start the engine once, a stop during startup waits and then stops, and a start during shutdown waits and then starts a new generation.

`StartAsync()` and `ReloadListenerAsync()` reject a server in `Faulted`. Call `StopAsync()` to retry cleanup; after it succeeds, `State` is `Stopped` and the server can be started again.


## Instantiation

You can create a server from an `IPAddress + port` or from an `EndPoint`.

```csharp
using System.Net;
using SimpleW;

var server1 = new SimpleWServer(IPAddress.Loopback, 8080);

var server2 = new SimpleWServer(new IPEndPoint(IPAddress.Any, 8081));
```


## Configuration

`Configure(Action<SimpleWSServerOptions>)` lets you customize server options while `State == SimpleWServerState.Stopped`.

> Important: calling `Configure(...)` during any lifecycle transition or while the server is running throws an exception.

```csharp
server.Configure(options => {
    options.MaxRequestBodySize = 100 * 1024 * 1024; // 100Mo
    options.SessionTimeout = TimeSpan.FromSeconds(30);
});
```

Options are automatically validated/normalized when starting (`StartAsync` calls `Options.ValidateAndNormalize()`).

::: info
See all [options](../reference/simplewserveroptions.md).
:::


## Optional: lifecycle state callbacks (fluent)

You can observe every server state transition with:

- `OnStateChanged(Action<SimpleWServer, SimpleWServerState>)`
- `OnStateChanged(Func<SimpleWServer, SimpleWServerState, Task>)`

Subscribing does not emit the initial `Stopped` state. Callbacks run sequentially in registration order, and asynchronous callbacks are awaited before the transition completes. Callback exceptions are logged without interrupting the lifecycle or the remaining subscribers.

Lifecycle methods must not be called from these callbacks; such reentrant calls are rejected to prevent a deadlock.

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080)
                .OnStateChanged((s, state) => Console.WriteLine($"Server {s.Port} state: {state}"));

await server.RunAsync();
```


## Optional: configure or replace the default Engine

By default, `SimpleWServer` uses `SimpleWEngine` as its network engine.

Configure the default socket engine with `UseEngine(Action<SimpleWEngineOptions>)` before starting the server.

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseEngine(options => {
    options.TcpNoDelay = true;
    options.ReuseAddress = true;
    options.TcpKeepAlive = true;
});
```

If you need a custom transport/listener implementation, replace it with `UseEngine(ISimpleWEngine)`.

```csharp
server.UseEngine(new MyCustomEngine());
```

Your custom engine only needs to implement [`ISimpleWEngine`](../reference/isimplewengine.md). The engine owns its accept loop; for each accepted connection, wrap it as an `ISimpleWEngine` and call `server.CreateSessionAsync(...)`.

Typical use cases :
- Customize how incoming connections are accepted
- Experiment with another low-level listener implementation
- Extend the default engine behavior in a dedicated class


## Optional: replace the default Router

By default, SimpleW uses the built-in `Router` implementation.

In advanced scenarios, you can replace the router with a custom implementation.

```csharp
server.UseRouter(new MyCustomRouter());
```

::: info
When the router is replaced, any registered route is lost.
:::

Typical use cases :
- Implement a custom routing strategy
- Wrap the default router to add cross-cutting features
- Experiment with alternative routing pipelines


## Optional: configure default Principal

By default, requests use an anonymous principal.

If you want the server to resolve a default principal for each request, you can use `ConfigurePrincipalResolver(...)`.

```csharp
server.ConfigurePrincipalResolver(session => {
    if (session.Request.Headers.TryGetValue("X-User", out string? user) && !string.IsNullOrWhiteSpace(user)) {
        return new HttpPrincipal(new HttpIdentity(
            isAuthenticated: true,
            authenticationType: "Header",
            identifier: user,
            name: user,
            email: null,
            roles: [ "user" ],
            properties: null
        ));
    }

    return HttpPrincipal.Anonymous;
});
```

This is useful for simple global identity resolution.
For shared authentication and authorization rules, middleware is usually a better fit.

See the [Principal guide](./principal.md).


## Starting the server

### StartAsync (non-blocking)

`StartAsync()` starts listening and returns immediately.

```csharp
await server.StartAsync();

// app continues running...
Console.WriteLine("Server started!");
```

This is useful if you already have your own host loop (service, worker, etc.)
and you just want to start/stop the server as part of a bigger lifecycle.


### RunAsync (blocking)

`RunAsync()` is a convenience method: it starts the server and then waits
until the server is stopped or the provided token is cancelled.

```csharp
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await server.RunAsync(cts.Token);
```


## Stopping the server

`StopAsync()` triggers shutdown. Internally it :
- cancels the lifetime token
- closes the listen socket
- closes all sessions
- disposes timers and telemetry
- resets server state

```csharp
await server.StopAsync();
```

Calling `StopAsync()` multiple times is safe. Concurrent calls are serialized and the engine is stopped only once. If an engine stop fails, local cleanup still completes, `State` becomes `Faulted`, and a later `StopAsync()` retries engine cleanup.


## Reloading the listener at runtime

`ReloadListenerAsync(...)` lets you change listening settings _while the server is running_.

- The server must already be started, otherwise it throws.
- It temporarily stops accepting new connections (closes the listen socket), applies your `reconfigure(server)` callback, then starts listening again.
- Existing sessions are not the listener: reloading focuses on the **accept loop + endpoint**. Engine-specific TLS settings can also be changed if the selected engine exposes that API.

```csharp
await server.ReloadListenerAsync(s => {
    s.UseAddress(IPAddress.Any);
    s.UsePort(9090);
    // Optional for the default socket engine:
    // if (s.Engine is SimpleWEngine engine) engine.UseHttps(newSslContext);
});
```

If the reload fails, the server attempts a best-effort rollback to the previous endpoint and throws a `ListenerReloadException`.

```csharp
try {
    await server.ReloadListenerAsync(s => s.UsePort(9090));
}
catch (ListenerReloadException ex) when (ex.ListenerRestored) {
    // The requested configuration failed, but the previous listener is active again.
    Debug.Assert(server.State == SimpleWServerState.Started);
    Console.Error.WriteLine(ex.ReloadException);
}
catch (ListenerReloadException ex) {
    // Both the reload and the rollback failed. The listener is unavailable.
    Debug.Assert(server.State == SimpleWServerState.Faulted);
    Console.Error.WriteLine(ex.ReloadException);
    Console.Error.WriteLine(ex.RollbackException);
}
```

- `ReloadException` is the original error raised while applying or starting the new listener.
- `RollbackException` is `null` when the previous listener was restored; otherwise it contains the restoration error.
- `ListenerRestored` is `true` only when the rollback completed successfully.

When `ListenerRestored` is `true`, `State` is `Started`. When both reload and rollback fail, `State` is `Faulted`; use `StopAsync()` to attempt recovery before starting again.

Restoring the listener keeps the server available, but the requested reload still failed and is therefore always reported to the caller.


## Procedural vs Fluent usage

### Procedural style

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);

server.Configure(options => { /* ... */ });

server.MapGet("/", ctx => "Hello");
await server.RunAsync();
```

### Fluent style

Most configuration methods return `SimpleWServer`, so you can chain them :

```csharp
await new SimpleWServer(IPAddress.Any, 8080)
    .Configure(options => { /* ... */ })
    .MapGet("/", ctx => "Hello")
    .RunAsync();
```
