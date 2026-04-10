# ISimplewEngine

## Signature

An `ISimplewEngine` is an interface with the following method:

```csharp
/// <summary>
/// Engine display name.
/// </summary>
string Name { get; }
```

```csharp
/// <summary>
/// Start the engine and begin accepting connections.
/// Return the effective bound endpoint when it differs from the configured one.
/// </summary>
/// <param name="server"></param>
/// <param name="options"></param>
/// <param name="connectionHandler"></param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
Task<EndPoint?> StartAsync(
    SimpleWServer server,
    SimpleWSServerOptions options,
    Func<Socket, Task> connectionHandler,
    CancellationToken cancellationToken = default
);
```

```csharp
/// <summary>
/// Stop the engine and release its listener resources.
/// </summary>
/// <param name="server"></param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default);
```

See more [examples](../guide/server.md#optional-replace-the-default-engine).
