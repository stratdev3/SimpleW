# Server

[`SimpleWServer`](../reference/simplewserver.md) is the main entry point to host a SimpleW Web server.
You can use it in a **procedural** style (step-by-step calls) or in a **fluent** style (chaining calls).


## Lifecycle actions

| Method | Behavior |
|---|---|
| `StartAsync(ct)` | Starts listening **without blocking** (returns immediately). Validates options before starting. |
| `RunAsync(ct)` | Starts the server and **blocks** until the server is stopped / cancelled. |
| `StopAsync()` | Stops listening, closes sessions, disposes internal resources. |
| `ReloadListenerAsync(reconfigure, ct)` | Hot-reloads the **listener** (endpoint / TLS, etc.) while the server is running. |


## Instantiation

You can create a server from an `IPAddress + port` or from an `EndPoint`.

```csharp
using System.Net;
using SimpleW;

var server1 = new SimpleWServer(IPAddress.Loopback, 8080);

var endpoint = new IPEndPoint(IPAddress.Any, 8081);
var server2 = new SimpleWServer(endpoint);
```


## Configuration

`Configure(Action<SimpleWSServerOptions>)` lets you customize server options **before** starting.

> Important: calling `Configure(...)` after the server has started throws an exception.

```csharp
server1.Configure(options => {
    // Example: set options here (depends on SimpleWSServerOptions fields)
    options.TcpNoDelay = true;
    optiosn.MaxRequestBodySize = 100 * 1024 * 1024; // 100Mo
});
```

Options are automatically validated/normalized when starting (`StartAsync` calls `Options.ValidateAndNormalize()`).

::: info
See all [options](../reference/simplewserveroptions.md).
:::


## Optional: lifecycle callbacks (fluent)

You can hook into lifecycle events :
- `OnStarted(Action<SimpleWServer>)` / `OnStarted(Func<SimpleWServer, Task>)`
- `OnStopped(Action<SimpleWServer>)` / `OnStopped(Func<SimpleWServer, Task>)`

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080)
                .OnStarted(s => Console.WriteLine("Listening!"))
                .OnStopped(s => Console.WriteLine("Stopped!"));

await server.RunAsync();
```


## Starting the server

### StartAsync (non-blocking)

`StartAsync()` starts listening and returns immediately.

```csharp
await server1.StartAsync();

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

await server1.RunAsync(cts.Token);
```


## Stopping the server

`StopAsync()` triggers shutdown. Internally it :
- cancels the lifetime token
- closes the listen socket
- closes all sessions
- disposes timers and telemetry
- resets server state

```csharp
await server1.StopAsync();
```

Calling `StopAsync()` multiple times is safe (it returns early if already stopped/stopping).


## Reloading the listener at runtime

`ReloadListenerAsync(...)` lets you change listening settings _while the server is running_.

- The server must already be started, otherwise it throws.
- It temporarily stops accepting new connections (closes the listen socket), applies your `reconfigure(server)` callback, then starts listening again.
- Existing sessions are not the listener: reloading focuses on the **accept loop + endpoint/TLS**.

```csharp
await server1.ReloadListenerAsync(s => {
    s.UseAddress(IPAddress.Any);
    s.UsePort(9090);
    // Optional: switch TLS config too
    // s.UseHttps(newSslContext);
});
```

If the reload fails, it will attempt a best-effort rollback to the previous endpoint/TLS
and re-listen, then rethrow the exception.


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
    // .UseHttps(sslContext)
    .RunAsync();
```
