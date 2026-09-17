# SimpleW Migration Guide v26.0..v26.1

## Notes

SimpleW v26.1 rewrites the network engine contract so that alternative engines can own the complete connection I/O pipeline.

Applications that only use the high-level server, routing, request, and response APIs should need a few mechanical changes. Applications that provide a custom `ISimpleWEngine`, access the session socket directly, or manage TLS manually require a larger migration.

This guide covers the core `SimpleW` package. Addons document their own changes in their package changelogs.


## Quick reference

| v26.0 | v26.1 |
|---|---|
| `server.IsStarted` | `server.State == SimpleWServerState.Started` |
| `server.IsStopping` | `server.State == SimpleWServerState.Stopping` |
| `server.IsListenerReloading` | `server.State == SimpleWServerState.Reloading` |
| `server.OnStarted(...)` / `server.OnStopped(...)` | `server.OnStateChanged(...)` |
| Socket options in `server.Configure(...)` | `server.UseEngine(...)` |
| `server.UseHttps(...)` / `server.DisableHttps()` | `SimpleWEngine.UseHttps(...)` / `SimpleWEngine.DisableHttps()` |
| `session.IsSsl` | `session.IsEncrypted` |
| `session.ClientCertificate` | Engine-neutral client certificate metadata |
| `session.Socket` / `session.TransportStream` | Endpoints and transport APIs |
| `server.EnableTelemetry()` / `server.DisableTelemetry()` | `server.ConfigureTelemetry(...)` |


## Code breaking changes

### Server lifecycle

`SimpleWServer` now exposes one thread-safe lifecycle state instead of three independent Boolean properties.

Before:

```csharp
if (server.IsStarted && !server.IsStopping) {
    // The server is accepting work.
}

if (server.IsListenerReloading) {
    // The listener is being reloaded.
}
```

After:

```csharp
if (server.State == SimpleWServerState.Started) {
    // The server is accepting work.
}

if (server.State == SimpleWServerState.Reloading) {
    // The listener is being reloaded.
}
```

The available states are `Stopped`, `Starting`, `Started`, `Reloading`, `Stopping`, and `Faulted`. Lifecycle operations are serialized. If an operation leaves the server in `Faulted`, stop it successfully before starting it again:

```csharp
if (server.State == SimpleWServerState.Faulted) {
    await server.StopAsync();
}

await server.StartAsync();
```

See [Server lifecycle](./server.md#lifecycle-state).


### Lifecycle callbacks

`OnStarted(...)` and `OnStopped(...)` have been replaced by `OnStateChanged(...)`.

Before:

```csharp
server
    .OnStarted(s => Console.WriteLine($"Started on {s.Port}"))
    .OnStopped(s => Console.WriteLine("Stopped"));
```

After:

```csharp
server.OnStateChanged((s, state) => {
    if (state == SimpleWServerState.Started) {
        Console.WriteLine($"Started on {s.Port}");
    }
    else if (state == SimpleWServerState.Stopped) {
        Console.WriteLine("Stopped");
    }
});
```

Synchronous and asynchronous callbacks run in registration order. Async callbacks are awaited. Exceptions are logged and do not stop the remaining callbacks.

::: warning
State callbacks are part of the serialized lifecycle transition. Do not call `StartAsync()`, `RunAsync()`, `StopAsync()`, or `ReloadListenerAsync()` from an `OnStateChanged(...)` callback; these calls throw `InvalidOperationException`.
:::


### Default engine options

Socket and listener settings have moved from `SimpleWSServerOptions` to `SimpleWEngineOptions`.

Before:

```csharp
server.Configure(options => {
    options.ListenBacklog = 8192;
    options.TcpNoDelay = true;
    options.ReuseAddress = true;
    options.TcpKeepAlive = true;
});
```

After:

```csharp
server.UseEngine(options => {
    options.ListenBacklog = 8192;
    options.TcpNoDelay = true;
    options.ReuseAddress = true;
    options.TcpKeepAlive = true;
});
```

`server.Configure(...)` still configures HTTP and session settings such as request limits and session timeouts. Configure both option groups before starting the server.

See [Replacing Core Components](./replacing-core-components.md#network-engine).


### TLS ownership

TLS is now owned by the network engine. The default `SimpleWEngine` performs the TLS handshake before it creates the HTTP session.

Before:

```csharp
server.UseHttps(sslContext);
```

After:

```csharp
server.UseEngine(options => {
    options.SslContext = sslContext;
});
```

During a listener reload, update the engine rather than the server:

Before:

```csharp
await server.ReloadListenerAsync(s => {
    s.DisableHttps();
    s.UsePort(8080);
});
```

After:

```csharp
await server.ReloadListenerAsync(s => {
    if (s.Engine is not SimpleWEngine engine) {
        throw new InvalidOperationException("The configured engine must handle the TLS update.");
    }

    engine.DisableHttps();
    s.UsePort(8080);
});
```

Use `SimpleWEngine.UseHttps(...)` in the same way when installing a renewed certificate. Alternative engines expose their own TLS configuration mechanism.

See [TLS Certificates](./tls-certificates.md).


### HttpSession

`HttpSession` no longer exposes the underlying .NET socket or stream.

#### Encryption and client certificates

Before:

```csharp
if (session.IsSsl && session.ClientCertificate != null) {
    Console.WriteLine(session.ClientCertificate.Subject);
}
```

After:

```csharp
if (session.IsEncrypted && session.IsClientCertificateAuthenticated == true) {
    Console.WriteLine(session.ClientCertificateSubject);
    Console.WriteLine(session.ClientCertificateEmailAddress);
    Console.WriteLine(session.NegotiatedApplicationProtocol);
}
```

`ClientCertificateSubject` uses the TLS engine's native textual format. Do not treat it as a portable identifier across different TLS engines.

#### Socket and stream access

Use the engine-neutral properties and methods instead of `Socket` and `TransportStream`:

```csharp
EndPoint? local = session.LocalEndPoint;
EndPoint? remote = session.RemoteEndPoint;

await session.AbortConnectionAsync(reset: true);
```

Protocol modules and custom engines should use `ISimpleWTransportInput` and `ISimpleWTransportOutput`. Application handlers should normally use `HttpRequest`, `HttpResponse`, and `AbortConnectionAsync(...)` rather than accessing the transport directly.

The public constructor has also changed from a socket-based constructor to:

```csharp
public HttpSession(
    SimpleWServer server,
    ISimpleWEngine transport,
    ArrayPool<byte> bufferPool
);
```

Custom engines should normally call `server.CreateSessionAsync(transport)` instead of constructing a session themselves.

See the [HttpSession reference](../reference/httpsession.md).


### Telemetry lifecycle

`EnableTelemetry()` and `DisableTelemetry()` have been removed.

Before:

```csharp
server.EnableTelemetry();
```

After:

```csharp
server.ConfigureTelemetry(options => {
    options.Enabled = true;
});
```

Telemetry is created when the server starts and disposed when it stops. Configure it before starting the server; it follows every subsequent server lifecycle generation automatically.

See [Observability](./observability.md).


### Listener reload failures

`ReloadListenerAsync(...)` now preserves both the reload failure and a possible rollback failure in `ListenerReloadException`.

```csharp
try {
    await server.ReloadListenerAsync(s => s.UsePort(8443));
}
catch (ListenerReloadException ex) when (ex.ListenerRestored) {
    // The requested reload failed, but the previous listener is running again.
    Console.WriteLine(ex.ReloadException.Message);
}
catch (ListenerReloadException ex) {
    // Both the requested reload and the rollback failed.
    Console.WriteLine(ex.ReloadException.Message);
    Console.WriteLine(ex.RollbackException?.Message);

    // The server is Faulted and must be stopped successfully before restart.
    await server.StopAsync();
}
```

`InnerException` references `ReloadException`. A restored previous listener does not turn the requested reload into a successful operation.

See [Listener reload](./server.md#reloading-the-listener-at-runtime).


### Custom network engines

The `ISimpleWEngine` listener signature has changed.

Before:

```csharp
Task<EndPoint?> StartAsync(
    SimpleWServer server,
    SimpleWSServerOptions options,
    Func<Socket, Task> connectionHandler,
    CancellationToken cancellationToken = default
);
```

After:

```csharp
Task<EndPoint?> StartAsync(
    SimpleWServer server,
    ArrayPool<byte> bufferPool,
    CancellationToken cancellationToken = default
);
```

The listener now owns accepted connection I/O. For every accepted connection, wrap the connection in an `ISimpleWEngine` transport and give it to the server:

```csharp
ISimpleWEngine transport = CreateAcceptedTransport(connection, bufferPool);
await server.CreateSessionAsync(transport);
```

An accepted transport exposes its local and remote endpoints, encryption state, `Input`, `Output`, graceful close, abort, optional features, and disposal. Output implementations also own the single-writer invariant and must write the complete supplied buffer or throw.

Read [Replacing Core Components](./replacing-core-components.md#network-engine) and the complete [`ISimpleWEngine` reference](../reference/isimplewengine.md) before migrating a custom engine.


## Compatibility changes

### Response metadata validation

Public `HttpResponse` methods now reject unsafe response metadata with `ArgumentException`:

- header names must use the HTTP token grammar;
- header values, status text, and content types must contain printable ASCII characters;
- cookie names, values, paths, and domains must follow their HTTP cookie grammar;
- an invalid custom `Content-Length` now throws `ArgumentException`.

Do not copy untrusted input directly into a header or cookie. Validate it first and use an appropriate ASCII-safe representation, such as a well-defined token or Base64Url value, when arbitrary data must be transported.


### WebSocket protocol validation

The WebSocket implementation now rejects malformed client frames that v26.0 could tolerate, including unmasked client frames, reserved bits, unknown opcodes, fragmented or oversized control frames, non-minimal payload lengths, and invalid close payloads.

RFC-compliant WebSocket clients require no migration. Custom clients must mask every client-to-server frame and emit valid control and close frames.
