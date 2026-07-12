using System.Buffers;
using System.Net;
using System.Security.Cryptography.X509Certificates;


namespace SimpleW {

    /// <summary>
    /// HTTP engine contract used by <see cref="SimpleWServer"/> to own accept, read, dispatch, write, and connection transport details.
    /// </summary>
    public interface ISimpleWEngine : IDisposable {

        /// <summary>
        /// Engine display name.
        /// </summary>
        string Name => GetType().Name;

        /// <summary>
        /// Start the engine and begin accepting connections.
        /// Return the effective bound endpoint when it differs from the configured one.
        /// Listener engines override this member.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<EndPoint?> StartAsync(
            SimpleWServer server,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException($"{Name} cannot accept connections.");

        /// <summary>
        /// Stop the engine and release its listener resources.
        /// Listener engines override this member.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>
        /// Local endpoint when this engine owns a connection endpoint.
        /// </summary>
        EndPoint? LocalEndPoint => null;

        /// <summary>
        /// Remote endpoint when this engine owns an accepted connection.
        /// </summary>
        EndPoint? RemoteEndPoint => null;

        /// <summary>
        /// Gets whether this engine already exposes encrypted bytes.
        /// </summary>
        bool IsEncrypted => false;

        /// <summary>
        /// Engine input for an accepted connection.
        /// </summary>
        ISimpleWTransportInput Input => throw new NotSupportedException($"{Name} does not expose connection input.");

        /// <summary>
        /// Engine output for an accepted connection.
        /// </summary>
        ISimpleWTransportOutput Output => throw new NotSupportedException($"{Name} does not expose connection output.");

        /// <summary>
        /// Tries to check whether the remote peer is already closed.
        /// Returns false when the engine has no cheap probing capability.
        /// </summary>
        /// <param name="isClosed"></param>
        bool TryCheckConnectionClosed(out bool isClosed) {
            isClosed = false;
            return false;
        }

        /// <summary>
        /// Closes the accepted connection gracefully.
        /// </summary>
        /// <param name="cancellationToken"></param>
        ValueTask CloseAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        /// <summary>
        /// Aborts the accepted connection. When reset is true, implementations should use an abortive close if supported.
        /// </summary>
        /// <param name="reset"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) => CloseAsync(cancellationToken);

        /// <summary>
        /// Gets an optional feature exposed by this engine.
        /// </summary>
        /// <typeparam name="TFeature"></typeparam>
        /// <returns></returns>
        TFeature? GetFeature<TFeature>() where TFeature : class => null;

        /// <summary>
        /// Releases engine resources.
        /// </summary>
        void IDisposable.Dispose() {
        }

    }

    /// <summary>
    /// Transport input that exposes native buffered data.
    /// </summary>
    public interface ISimpleWTransportInput {

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

    }

    /// <summary>
    /// Transport output writer.
    /// Implementations own the per-connection single-writer invariant.
    /// </summary>
    public interface ISimpleWTransportOutput {

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
        ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes both segments to the transport.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
        /// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
        ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default);

    }

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

    /// <summary>
    /// Optional transport feature used by the server to enable TLS before a session is created.
    /// </summary>
    public interface ISimpleWTransportTlsFeature {

        /// <summary>
        /// Upgrades the current transport to TLS.
        /// </summary>
        /// <param name="sslContext"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        ValueTask<ISimpleWEngine> UseTlsAsync(SslContext sslContext, CancellationToken cancellationToken = default);

        /// <summary>
        /// Client certificate when provided by the peer.
        /// </summary>
        X509Certificate2? ClientCertificate { get; }

    }

    /// <summary>
    /// Result returned by a transport input read.
    /// </summary>
    public readonly record struct SimpleWTransportReadResult(ReadOnlySequence<byte> Buffer, bool IsCompleted);

}
