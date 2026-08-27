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
/// If startup fails, the engine must release every resource acquired by that startup attempt before throwing.
/// </summary>
/// <param name="server"></param>
/// <param name="bufferPool"></param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
Task<EndPoint?> StartAsync(
    SimpleWServer server,
    ArrayPool<byte> bufferPool,
    CancellationToken cancellationToken = default
);
```

A successful return transfers responsibility for the running listener to `SimpleWServer`. If `StartAsync()` throws, the engine must roll back its own partial startup; the server will not call `StopAsync()` for an unsuccessful start.

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

You do not implement a separate transport contract. `ISimpleWTransportInput` and `ISimpleWTransportOutput` expose input and output, while `ISimpleWTransportDeferredFlushFeature` and `ISimpleWTransportTlsFeature` provide optional transport capabilities. Output writes return the number of bytes written to the local transport.

```csharp
/// <summary>
/// Local endpoint when this engine owns a connection endpoint.
/// </summary>
EndPoint? LocalEndPoint { get; }

/// <summary>
/// Remote endpoint when this engine owns an accepted connection.
/// </summary>
EndPoint? RemoteEndPoint { get; }

/// <summary>
/// Gets whether this engine already exposes encrypted bytes.
/// </summary>
bool IsEncrypted { get; }

/// <summary>
/// Engine input for an accepted connection.
/// </summary>
ISimpleWTransportInput Input { get; }

/// <summary>
/// Engine output for an accepted connection.
/// </summary>
ISimpleWTransportOutput Output { get; }

/// <summary>
/// Tries to check whether the remote peer is already closed.
/// Returns false when the engine has no cheap probing capability.
/// </summary>
/// <param name="isClosed"></param>
bool TryCheckConnectionClosed(out bool isClosed);

/// <summary>
/// Closes the accepted connection gracefully.
/// </summary>
/// <param name="cancellationToken"></param>
ValueTask CloseAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Aborts the accepted connection. When reset is true, implementations should use an abortive close if supported.
/// </summary>
/// <param name="reset"></param>
/// <param name="cancellationToken"></param>
/// <returns></returns>
ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default);

/// <summary>
/// Gets an optional feature exposed by this engine.
/// </summary>
/// <typeparam name="TFeature"></typeparam>
/// <returns></returns>
TFeature? GetFeature<TFeature>() where TFeature : class;

/// <summary>
/// Releases engine resources.
/// </summary>
void IDisposable.Dispose() {
}
```

## Transport Input Reads

`ISimpleWTransportInput.ReadAsync(...)` returns the current buffered bytes and whether the input is completed. After parsing, SimpleW advances the input with the consumed and examined byte counts.

```csharp
/// <summary>
/// Reads or returns currently buffered bytes.
/// </summary>
/// <param name="cancellationToken"></param>
/// <returns></returns>
ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Advances the input by the number of consumed and examined bytes from the last buffer.
/// </summary>
/// <param name="consumedBytes"></param>
/// <param name="examinedBytes"></param>
void AdvanceTo(long consumedBytes, long examinedBytes);
```

```csharp
/// <summary>
/// Result returned by a transport input read.
/// </summary>
public readonly record struct SimpleWTransportReadResult(
    ReadOnlySequence<byte> Buffer,
    bool IsCompleted
);
```

## Transport Output Writes

`ISimpleWTransportOutput.WriteAsync(...)` returns the number of bytes written to the local transport. Implementations own the per-connection single-writer invariant, fail fast with `InvalidOperationException` when a write is already in progress, and must write the full input or throw.

```csharp
/// <summary>
/// Writes all bytes to the transport.
/// </summary>
/// <param name="buffer"></param>
/// <param name="cancellationToken"></param>
/// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
/// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

/// <summary>
/// Writes all provided segments to the transport.
/// </summary>
/// <param name="segments"></param>
/// <param name="cancellationToken"></param>
/// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
/// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default);

/// <summary>
/// Writes both segments to the transport.
/// </summary>
/// <param name="header"></param>
/// <param name="body"></param>
/// <param name="cancellationToken"></param>
/// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
/// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default);
```

## Optional Deferred Flush Feature

An output that stages bytes during `WriteAsync(...)` can also implement `ISimpleWTransportDeferredFlushFeature`. The feature must be implemented by the same object returned from `ISimpleWEngine.Output`; SimpleW detects it directly with `transport.Output is ISimpleWTransportDeferredFlushFeature`, not through `GetFeature<TFeature>()`.

```csharp
/// <summary>
/// Optional transport feature used to flush bytes staged during the current read batch.
/// </summary>
public interface ISimpleWTransportDeferredFlushFeature {

    /// <summary>
    /// Flushes bytes staged by previous WriteAsync calls.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask FlushDeferredAsync(CancellationToken cancellationToken = default);
}
```

`FlushDeferredAsync(...)` completes when all bytes staged by earlier `WriteAsync(...)` calls have been handed to the underlying transport. It should complete immediately when no bytes are pending and honor the supplied cancellation token.

SimpleW calls this feature at protocol boundaries where deferred bytes must become visible to the peer:

- after processing the current HTTP read batch;
- after the WebSocket upgrade response and after each WebSocket frame;
- after the SSE response headers and after each SSE event or comment.

Outputs that send every write immediately do not need to implement this feature. The Ioxide transport implements it to defer and coalesce writes while preserving the flush points required by HTTP, WebSocket, and SSE.

## Optional TLS Feature

TLS handshakes are owned by listener engines before they call `SimpleWServer.CreateSessionAsync(...)`. An accepted encrypted transport can expose `ISimpleWTransportTlsFeature` through `GetFeature<ISimpleWTransportTlsFeature>()` when it provides TLS metadata.

```csharp
public interface ISimpleWTransportTlsFeature {

    /// <summary>
    /// Application protocol selected during the TLS handshake, or null when none was negotiated.
    /// </summary>
    string? NegotiatedApplicationProtocol { get; }

    /// <summary>
    /// Subject of the authenticated client certificate in the transport's native format.
    /// </summary>
    string? ClientCertificateSubject { get; }

    /// <summary>
    /// Email address of the authenticated client certificate, or null when unavailable.
    /// </summary>
    string? ClientCertificateEmailAddress { get; }

    /// <summary>
    /// Gets whether the client presented a certificate accepted by the configured TLS policy.
    /// </summary>
    bool IsClientCertificateAuthenticated { get; }
}
```

`ClientCertificateSubject` is the subject validated by the TLS transport. Its representation is native to the engine and must not be parsed naively or compared directly between .NET and OpenSSL engines.


See more [examples](../guide/replacing-core-components.md#network-engine).
