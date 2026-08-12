using System.Buffers;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using ioxide;
using ioxide.tls;
using ioxide.utils;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// Direct SimpleW adapter over the native ioxide OpenSSL TLS API.
    /// </summary>
    internal sealed class IoxideTlsConnection :
        ISimpleWEngine,
        ISimpleWTransportInput,
        ISimpleWTransportOutput,
        ISimpleWTransportDeferredFlushFeature,
        ISimpleWTransportTlsFeature,
        IValueTaskSource<SimpleWTransportReadResult> {

        private const int InitialPlaintextBufferSize = 16 * 1024;
        private const int ShutBoth = 2;

        private static readonly Func<TlsSession, string?> GetPeerSubject = CreatePeerSubjectAccessor();

        private readonly TcpConnection _connection;
        private readonly TlsSession _tlsSession;
        private readonly EndPoint _localEndPoint;
        private readonly ArrayPool<byte> _bufferPool;
        private readonly bool _deferFlush;
        private readonly Action _onRecvReady;

        private byte[]? _plaintext;
        private int _plaintextStart;
        private int _plaintextEnd;
        private long _examinedBytes;
        private ReadOnlySequence<byte> _buffer;
        private long _advanceConsumed;
        private long _advanceExamined;
        private bool _hasRead;
        private bool _readPending;
        private bool _connectionClosed;
        private bool _hasDeferredWrites;
        private int _writing;
        private int _disposed;
        private int _completed;

        private ManualResetValueTaskSourceCore<SimpleWTransportReadResult> _readSignal = new() {
            RunContinuationsAsynchronously = false
        };
        private ValueTaskAwaiter<RecvSnapshot> _pendingRead;

        /// <inheritdoc />
        public EndPoint? LocalEndPoint => _localEndPoint;

        /// <inheritdoc />
        public EndPoint? RemoteEndPoint => null;

        /// <inheritdoc />
        public bool IsEncrypted => true;

        /// <inheritdoc />
        public ISimpleWTransportInput Input => this;

        /// <inheritdoc />
        public ISimpleWTransportOutput Output => this;

        /// <inheritdoc />
        public string? NegotiatedApplicationProtocol { get; }

        /// <inheritdoc />
        public string? ClientCertificateSubject { get; }

        /// <inheritdoc />
        public string? ClientCertificateEmailAddress => null;

        /// <inheritdoc />
        public bool IsClientCertificateAuthenticated { get; }

        /// <summary>
        /// Initializes a raw OpenSSL transport over an accepted ioxide TCP connection.
        /// </summary>
        /// <param name="connection">Accepted ioxide TCP connection.</param>
        /// <param name="tlsSession">Completed OpenSSL server session.</param>
        /// <param name="localEndPoint">Local endpoint that accepted the connection.</param>
        /// <param name="bufferPool">Pool used to retain decrypted plaintext between reads.</param>
        /// <param name="deferFlush">Whether writes are flushed at the end of the current read batch.</param>
        /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
        public IoxideTlsConnection(TcpConnection connection, TlsSession tlsSession, EndPoint localEndPoint, ArrayPool<byte> bufferPool, bool deferFlush) {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _tlsSession = tlsSession ?? throw new ArgumentNullException(nameof(tlsSession));
            _localEndPoint = localEndPoint ?? throw new ArgumentNullException(nameof(localEndPoint));
            _bufferPool = bufferPool ?? throw new ArgumentNullException(nameof(bufferPool));
            _deferFlush = deferFlush;
            _onRecvReady = OnRecvReady;
            _plaintext = _bufferPool.Rent(InitialPlaintextBufferSize);

            NegotiatedApplicationProtocol = tlsSession.NegotiatedAlpn;
            ClientCertificateSubject = GetPeerSubject(tlsSession);
            IsClientCertificateAuthenticated = ClientCertificateSubject != null;

            AppendPlaintext(tlsSession.DrainPlaintext());
            _connectionClosed = tlsSession.Closed;
        }

        private static Func<TlsSession, string?> CreatePeerSubjectAccessor() {
            MethodInfo? getter = typeof(TlsSession).GetProperty("PeerSubject")?.GetMethod;
            return getter == null ? static _ => null : getter.CreateDelegate<Func<TlsSession, string?>>();
        }

        /// <inheritdoc />
        public ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            CommitAdvance();

            if (_readPending) {
                throw new InvalidOperationException("Only one ioxide TLS read can be pending per connection.");
            }

            long available = _plaintextEnd - _plaintextStart;
            if (available > _examinedBytes || _connectionClosed) {
                return new ValueTask<SimpleWTransportReadResult>(BuildReadResult());
            }

            while (true) {
                ValueTask<RecvSnapshot> read = _connection.ReadAsync();
                if (!read.IsCompletedSuccessfully) {
                    _readSignal.Reset();
                    _readPending = true;
                    _pendingRead = read.GetAwaiter();
                    _pendingRead.UnsafeOnCompleted(_onRecvReady);
                    return new ValueTask<SimpleWTransportReadResult>(this, _readSignal.Version);
                }

                if (Ingest(read.Result) || _connectionClosed) {
                    return new ValueTask<SimpleWTransportReadResult>(BuildReadResult());
                }
            }
        }

        private void OnRecvReady() {
            try {
                RecvSnapshot snapshot = _pendingRead.GetResult();
                if (!Ingest(snapshot) && !_connectionClosed) {
                    while (true) {
                        ValueTask<RecvSnapshot> read = _connection.ReadAsync();
                        if (!read.IsCompletedSuccessfully) {
                            _pendingRead = read.GetAwaiter();
                            _pendingRead.UnsafeOnCompleted(_onRecvReady);
                            return;
                        }

                        if (Ingest(read.Result) || _connectionClosed) {
                            break;
                        }
                    }
                }

                _readPending = false;
                _readSignal.SetResult(BuildReadResult());
            }
            catch (Exception ex) {
                _readPending = false;
                _readSignal.SetException(ex);
            }
        }

        private unsafe bool Ingest(in RecvSnapshot snapshot) {
            bool added = false;

            while (_connection.TryGetItem(snapshot, out SpscRecvRing.Item item)) {
                if (!item.HasBuffer) {
                    continue;
                }

                try {
                    if (item.Len > 0) {
                        ReadOnlySpan<byte> plaintext = _tlsSession.Decrypt(item.Ptr, item.Len);
                        if (!plaintext.IsEmpty) {
                            AppendPlaintext(plaintext);
                            added = true;
                        }
                    }
                }
                finally {
                    _connection.ReturnBuffer(in item);
                }
            }

            _connectionClosed |= snapshot.IsClosed || _tlsSession.Closed;
            if (!_connectionClosed) {
                _connection.ResetRead();
            }
            return added;
        }

        private void AppendPlaintext(ReadOnlySpan<byte> plaintext) {
            if (plaintext.IsEmpty) {
                return;
            }

            EnsurePlaintextCapacity(plaintext.Length);
            byte[] buffer = _plaintext ?? throw new ObjectDisposedException(nameof(IoxideTlsConnection));
            plaintext.CopyTo(buffer.AsSpan(_plaintextEnd));
            _plaintextEnd += plaintext.Length;
        }

        private void EnsurePlaintextCapacity(int additionalBytes) {
            byte[] buffer = _plaintext ?? throw new ObjectDisposedException(nameof(IoxideTlsConnection));
            if (buffer.Length - _plaintextEnd >= additionalBytes) {
                return;
            }

            if (_plaintextStart != 0) {
                int remaining = _plaintextEnd - _plaintextStart;
                buffer.AsSpan(_plaintextStart, remaining).CopyTo(buffer);
                _plaintextStart = 0;
                _plaintextEnd = remaining;
                if (buffer.Length - _plaintextEnd >= additionalBytes) {
                    return;
                }
            }

            int required = checked(_plaintextEnd + additionalBytes);
            int newSize = Math.Max(checked(buffer.Length * 2), required);
            byte[] replacement = _bufferPool.Rent(newSize);
            buffer.AsSpan(0, _plaintextEnd).CopyTo(replacement);
            _plaintext = replacement;
            _bufferPool.Return(buffer);
        }

        private SimpleWTransportReadResult BuildReadResult() {
            byte[] plaintext = _plaintext ?? throw new ObjectDisposedException(nameof(IoxideTlsConnection));
            _buffer = new ReadOnlySequence<byte>(plaintext, _plaintextStart, _plaintextEnd - _plaintextStart);
            _advanceConsumed = 0;
            _advanceExamined = 0;
            _hasRead = true;
            return new SimpleWTransportReadResult(_buffer, _connectionClosed);
        }

        /// <inheritdoc />
        public void AdvanceTo(long consumedBytes, long examinedBytes) {
            if (!_hasRead) {
                return;
            }

            long remaining = _buffer.Length - _advanceConsumed;
            long consumed = Clamp(consumedBytes, remaining);
            long examined = Clamp(examinedBytes, remaining);
            if (examined < consumed) {
                examined = consumed;
            }

            long consumedBefore = _advanceConsumed;
            _advanceConsumed += consumed;
            _advanceExamined = Math.Max(_advanceExamined, consumedBefore + examined);
        }

        private void CommitAdvance() {
            if (!_hasRead) {
                return;
            }

            long available = _plaintextEnd - _plaintextStart;
            long consumed = Clamp(_advanceConsumed, available);
            long examined = Clamp(Math.Max(_advanceExamined, consumed), available);

            _plaintextStart += (int)consumed;
            if (_plaintextStart == _plaintextEnd) {
                _plaintextStart = 0;
                _plaintextEnd = 0;
            }

            long remaining = _plaintextEnd - _plaintextStart;
            _examinedBytes = Math.Min(examined - consumed, remaining);
            _buffer = ReadOnlySequence<byte>.Empty;
            _advanceConsumed = 0;
            _advanceExamined = 0;
            _hasRead = false;
        }

        /// <inheritdoc />
        public ValueTask<long> WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        ) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            try {
                _tlsSession.Write(_connection, buffer.Span);
            }
            catch {
                ExitWrite();
                throw;
            }
            return FinishWrite(buffer.Length);
        }

        /// <inheritdoc />
        public ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(segments);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            long written = 0;
            try {
                foreach (ArraySegment<byte> segment in segments) {
                    if (segment.Array == null || segment.Count == 0) {
                        continue;
                    }
                    _tlsSession.Write(_connection, segment.AsSpan());
                    written += segment.Count;
                }
            }
            catch {
                ExitWrite();
                throw;
            }
            return FinishWrite(written);
        }

        /// <inheritdoc />
        public ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            try {
                if (header.Array != null && header.Count != 0) {
                    _tlsSession.Write(_connection, header.AsSpan());
                }
                if (body.Array != null && body.Count != 0) {
                    _tlsSession.Write(_connection, body.AsSpan());
                }
            }
            catch {
                ExitWrite();
                throw;
            }
            return FinishWrite((long)header.Count + body.Count);
        }

        private ValueTask<long> FinishWrite(long written) {
            if (_deferFlush) {
                _hasDeferredWrites |= written != 0;
                ExitWrite();
                return new ValueTask<long>(written);
            }

            ValueTask flush;
            try {
                flush = _connection.FlushAsync();
            }
            catch {
                ExitWrite();
                throw;
            }
            if (flush.IsCompletedSuccessfully) {
                try {
                    flush.GetAwaiter().GetResult();
                    return new ValueTask<long>(written);
                }
                finally {
                    ExitWrite();
                }
            }
            return AwaitFlushAndExitWriteAsync(flush, written);
        }

        private async ValueTask<long> AwaitFlushAndExitWriteAsync(ValueTask flush, long written) {
            try {
                await flush;
                return written;
            }
            finally {
                ExitWrite();
            }
        }

        /// <inheritdoc />
        public ValueTask FlushDeferredAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            if (!_deferFlush || !_hasDeferredWrites) {
                ExitWrite();
                return ValueTask.CompletedTask;
            }

            _hasDeferredWrites = false;
            ValueTask flush;
            try {
                flush = _connection.FlushAsync();
            }
            catch {
                ExitWrite();
                throw;
            }

            if (flush.IsCompletedSuccessfully) {
                try {
                    flush.GetAwaiter().GetResult();
                    return ValueTask.CompletedTask;
                }
                finally {
                    ExitWrite();
                }
            }
            return AwaitDeferredFlushAndExitWriteAsync(flush);
        }

        private async ValueTask AwaitDeferredFlushAndExitWriteAsync(ValueTask flush) {
            try {
                await flush;
            }
            finally {
                ExitWrite();
            }
        }

        /// <inheritdoc />
        public bool TryCheckConnectionClosed(out bool isClosed) {
            isClosed = _connectionClosed || _tlsSession.Closed;
            return true;
        }

        /// <inheritdoc />
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                _ = Shutdown(_connection.ClientFd, ShutBoth);
            }
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public TFeature? GetFeature<TFeature>() where TFeature : class {
            if (typeof(TFeature) == typeof(ISimpleWTransportDeferredFlushFeature)
                || typeof(TFeature) == typeof(ISimpleWTransportTlsFeature)) {
                return this as TFeature;
            }
            return null;
        }

        /// <inheritdoc />
        public void Dispose() {
            Interlocked.Exchange(ref _disposed, 1);
        }

        /// <summary>
        /// Completes TLS shutdown and releases all buffers retained by the connection.
        /// </summary>
        public void Complete() {
            if (Interlocked.Exchange(ref _completed, 1) != 0) {
                return;
            }

            Interlocked.Exchange(ref _disposed, 1);
            try { _tlsSession.Dispose(); }
            catch { }
            _ = Shutdown(_connection.ClientFd, ShutBoth);
            try {
                CommitAdvance();
            }
            catch {
            }

            byte[]? plaintext = Interlocked.Exchange(ref _plaintext, null);
            if (plaintext != null) {
                _bufferPool.Return(plaintext);
            }
            _plaintextStart = 0;
            _plaintextEnd = 0;
            _examinedBytes = 0;
            _buffer = ReadOnlySequence<byte>.Empty;
        }

        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent write on the same ioxide TLS connection.");
            }
        }

        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        private void ThrowIfDisposed() {
            if (Volatile.Read(ref _disposed) != 0) {
                throw new ObjectDisposedException(nameof(IoxideTlsConnection));
            }
        }

        private static long Clamp(long value, long maximum) {
            if (value <= 0) {
                return 0;
            }
            return value >= maximum ? maximum : value;
        }

        SimpleWTransportReadResult IValueTaskSource<SimpleWTransportReadResult>.GetResult(short token) {
            return _readSignal.GetResult(token);
        }

        ValueTaskSourceStatus IValueTaskSource<SimpleWTransportReadResult>.GetStatus(short token) {
            return _readSignal.GetStatus(token);
        }

        void IValueTaskSource<SimpleWTransportReadResult>.OnCompleted(
            Action<object?> continuation,
            object? state,
            short token,
            ValueTaskSourceOnCompletedFlags flags
        ) {
            _readSignal.OnCompleted(
                continuation,
                state,
                token,
                flags & ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext
            );
        }

        [DllImport("libc", EntryPoint = "shutdown", SetLastError = true)]
        private static extern int Shutdown(int socket, int how);

    }

}
