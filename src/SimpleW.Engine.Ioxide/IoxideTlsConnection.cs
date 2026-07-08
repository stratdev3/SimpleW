using System.Buffers;
using System.Net;
using ioxide;
using ioxide.tls;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// kTLS-backed ioxide transport for SimpleW.
    /// Inbound TLS records are decrypted through TlsSession; outbound writes stay plaintext and are encrypted by kernel TLS.
    /// </summary>
    internal sealed class IoxideTlsConnection : ISimpleWEngine, ISimpleWTransportInput, ISimpleWTransportOutput, ISimpleWTransportDeferredFlushFeature, ISimpleWTransportTlsFeature {

        private readonly Connection _connection;
        private readonly TlsSession _tls;
        private readonly EndPoint? _localEndPoint;
        private ReadOnlySequence<byte> _buffer;
        private byte[] _bufferArray = [];
        private bool _hasBuffer;
        private bool _completed;

        /// <summary>
        /// Local endpoint when this transport owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint => _localEndPoint;

        /// <summary>
        /// Remote endpoint when this transport owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint => null;

        /// <summary>
        /// Gets whether this transport already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted => true;

        /// <summary>
        /// Transport input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input => this;

        /// <summary>
        /// Transport output for an accepted connection.
        /// </summary>
        public ISimpleWTransportOutput Output => this;

        /// <summary>
        /// Client certificates are not exposed by the current kTLS path.
        /// </summary>
        public System.Security.Cryptography.X509Certificates.X509Certificate2? ClientCertificate => null;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tls"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="deferFlush"></param>
        public IoxideTlsConnection(Connection connection, TlsSession tls, EndPoint? localEndPoint, bool deferFlush = false) {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _tls = tls ?? throw new ArgumentNullException(nameof(tls));
            _localEndPoint = localEndPoint;
            _deferFlush = deferFlush;
            SetBuffer(_tls.DrainPlaintext(), completed: _tls.Closed);
        }

        #region ISimpleWTransportInput

        /// <summary>
        /// Read plaintext request bytes from the kTLS session.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
            if (_hasBuffer || _completed) {
                return CurrentResult();
            }

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                RecvSnapshot snapshot = await _connection.ReadAsync().ConfigureAwait(false);
                ArrayBufferWriter<byte>? writer = null;

                while (_connection.TryGetItem(snapshot, out var item)) {
                    try {
                        if (!item.HasBuffer) {
                            continue;
                        }

                        ReadOnlySpan<byte> plaintext;
                        unsafe {
                            plaintext = _tls.Decrypt(item.Ptr, item.Len);
                        }
                        if (!plaintext.IsEmpty) {
                            writer ??= new ArrayBufferWriter<byte>(plaintext.Length);
                            writer.Write(plaintext);
                        }
                    }
                    finally {
                        _connection.ReturnBuffer(in item);
                    }
                }

                bool closed = snapshot.IsClosed || _tls.Closed;
                _connection.ResetRead();

                if (writer != null && writer.WrittenCount > 0) {
                    SetBuffer(writer.WrittenSpan, completed: closed);
                    return CurrentResult();
                }

                if (closed) {
                    _completed = true;
                    return CurrentResult();
                }
            }
        }

        #endregion ISimpleWTransportInput

        #region ISimpleWTransportOutput

        private int _writing;
        private readonly bool _deferFlush;
        private int _hasDeferredWrites;

        /// <summary>
        /// Writes plaintext bytes; kernel TLS emits TLS records on the socket.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                cancellationToken.ThrowIfCancellationRequested();
                _connection.Write(buffer.Span);
                await FlushAfterWriteAsync().ConfigureAwait(false);
                return buffer.Length;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes plaintext segments; kernel TLS emits TLS records on the socket.
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                ArgumentNullException.ThrowIfNull(segments);
                cancellationToken.ThrowIfCancellationRequested();

                long written = 0;
                foreach (ArraySegment<byte> segment in segments) {
                    if (segment.Array == null || segment.Count <= 0) {
                        continue;
                    }
                    _connection.Write(segment.AsSpan());
                    written += segment.Count;
                }

                await FlushAfterWriteAsync().ConfigureAwait(false);
                return written;
            }
            finally {
                ExitWrite();
            }
        }

        /// <summary>
        /// Writes plaintext segments; kernel TLS emits TLS records on the socket.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            EnterWrite();
            try {
                cancellationToken.ThrowIfCancellationRequested();
                WriteToConnection(header);
                WriteToConnection(body);
                await FlushAfterWriteAsync().ConfigureAwait(false);
                return header.Count + body.Count;
            }
            finally {
                ExitWrite();
            }
        }

        private async ValueTask FlushAfterWriteAsync() {
            if (_deferFlush) {
                Volatile.Write(ref _hasDeferredWrites, 1);
                return;
            }

            await _connection.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Flushes writes staged by deferred-flush mode.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async ValueTask FlushDeferredAsync(CancellationToken cancellationToken = default) {
            if (!_deferFlush || Interlocked.Exchange(ref _hasDeferredWrites, 0) == 0) {
                return;
            }

            EnterWrite();
            try {
                cancellationToken.ThrowIfCancellationRequested();
                await _connection.FlushAsync().ConfigureAwait(false);
            }
            finally {
                ExitWrite();
            }
        }

        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent WriteAsync on same transport output");
            }
        }

        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        #endregion ISimpleWTransportOutput

        #region process


        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public TFeature? GetFeature<TFeature>() where TFeature : class {
            if (typeof(TFeature) == typeof(ISimpleWTransportTlsFeature)) {
                return this as TFeature;
            }
            if (typeof(TFeature) == typeof(ISimpleWTransportDeferredFlushFeature)) {
                return this as TFeature;
            }
            return null;
        }

        public void AdvanceTo(long consumedBytes, long examinedBytes) {
            if (!_hasBuffer) {
                return;
            }

            long length = _buffer.Length;
            long consumed = Clamp(consumedBytes, length);
            long examined = Clamp(examinedBytes, length);
            if (examined < consumed) {
                examined = consumed;
            }

            if (consumed >= length) {
                ClearBuffer();
                return;
            }

            _buffer = _buffer.Slice(consumed);
            _hasBuffer = !_buffer.IsEmpty;
        }

        #endregion process

        #region dispose

        private int _disposed;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) {
                return;
            }

            try { _tls.Dispose(); }
            catch { }
            try { _connection.MarkClosed(); }
            catch { }
            try { _connection.DecRef(); }
            catch { }
        }

        #endregion dispose

        #region helpers

        private SimpleWTransportReadResult CurrentResult() => new(_buffer, _completed);

        private void SetBuffer(ReadOnlySpan<byte> plaintext, bool completed) {
            _completed = completed;
            if (plaintext.IsEmpty) {
                ClearBuffer();
                return;
            }

            _bufferArray = plaintext.ToArray();
            _buffer = new ReadOnlySequence<byte>(_bufferArray);
            _hasBuffer = true;
        }

        private void ClearBuffer() {
            _bufferArray = [];
            _buffer = ReadOnlySequence<byte>.Empty;
            _hasBuffer = false;
        }

        private void WriteToConnection(ArraySegment<byte> source) {
            if (source.Array == null || source.Count <= 0) {
                return;
            }

            _connection.Write(source.AsSpan());
        }

        private static long Clamp(long value, long max) {
            if (value <= 0) {
                return 0;
            }
            if (value >= max) {
                return max;
            }
            return value;
        }

        #endregion helpers

    }

}