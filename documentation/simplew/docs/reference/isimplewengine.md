# ISimpleWEngine

## Signature

An `ISimpleWEngine` is the single network contract. A server engine starts and stops listening, then gives each accepted connection back to `SimpleWServer.CreateSessionAsync(...)` through the same interface.

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
/// <param name="cancellationToken"></param>
/// <returns></returns>
Task<EndPoint?> StartAsync(
    SimpleWServer server,
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

## Accepted Connection Members

The accepted connection is still an `ISimpleWEngine`. It exposes transport-style members on the same contract.

When an engine accepts a connection, it creates or wraps that connection as an `ISimpleWEngine` and calls:

```csharp
await server.CreateSessionAsync(acceptedConnection);
```

You do not implement a separate transport contract. `ISimpleWTransportInput`, `ISimpleWTransportOutput`, and `ISimpleWTransportTlsFeature` are small helper contracts used by an engine to expose input, output, and optional TLS support. Output writes return the number of bytes written to the local transport.

```csharp
EndPoint? LocalEndPoint { get; }
EndPoint? RemoteEndPoint { get; }
bool IsEncrypted { get; }
ISimpleWTransportInput Input { get; }
ISimpleWTransportOutput Output { get; }
bool TryCheckConnectionClosed(out bool isClosed);
ValueTask CloseAsync(CancellationToken cancellationToken = default);
ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default);
TFeature? GetFeature<TFeature>() where TFeature : class;
```

## Transport Input Reads

`ISimpleWTransportInput.ReadAsync(...)` returns the current buffered bytes and whether the input is completed. After parsing, SimpleW advances the input with the consumed and examined byte counts.

```csharp
ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default);
void AdvanceTo(long consumedBytes, long examinedBytes);
```

```csharp
public readonly record struct SimpleWTransportReadResult(
    ReadOnlySequence<byte> Buffer,
    bool IsCompleted
);
```

## Transport Output Writes

`ISimpleWTransportOutput.WriteAsync(...)` returns the number of bytes written to the local transport. Implementations own the per-connection single-writer invariant, fail fast with `InvalidOperationException` when a write is already in progress, and must write the full input or throw.

```csharp
ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default);
ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default);
```

## Optional TLS Feature

If the server is configured with `UseHttps(...)`, the accepted connection must expose `ISimpleWTransportTlsFeature` through `GetFeature<ISimpleWTransportTlsFeature>()`. SimpleW calls `UseTlsAsync(...)` before creating the `HttpSession`.

```csharp
ValueTask<ISimpleWEngine> UseTlsAsync(SslContext sslContext, CancellationToken cancellationToken = default);
X509Certificate2? ClientCertificate { get; }
```

See more [examples](../guide/server.md#optional-replace-the-default-engine).
