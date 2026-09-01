using System.Buffers;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using ioxide;
using ioxide.utils;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// Direct SimpleW adapter over the raw ioxide TCP API.
    /// </summary>
    internal sealed class IoxideTransport : ISimpleWEngine, ISimpleWTransportInput, ISimpleWTransportOutput, ISimpleWTransportDeferredFlushFeature, IValueTaskSource<SimpleWTransportReadResult> {

        #region ISimpleWEngine

        private readonly TcpConnection _connection;
        private readonly EndPoint _localEndPoint;

        private bool _connectionClosed;

        private int _disposed;
        private int _completed;

        /// <summary>
        /// Local endpoint when this engine owns a connection endpoint.
        /// </summary>
        public EndPoint? LocalEndPoint => _localEndPoint;

        /// <summary>
        /// Remote endpoint when this engine owns an accepted connection.
        /// </summary>
        public EndPoint? RemoteEndPoint => null;

        /// <summary>
        /// Gets whether this engine already exposes encrypted bytes.
        /// </summary>
        public bool IsEncrypted => false;

        /// <summary>
        /// Engine input for an accepted connection.
        /// </summary>
        public ISimpleWTransportInput Input => this;

        /// <summary>
        /// Engine output for an accepted connection.
        /// </summary>
        public ISimpleWTransportOutput Output => this;

        /// <summary>
        /// Tries to check whether the remote peer is already closed.
        /// Returns false when the engine has no cheap probing capability.
        /// </summary>
        /// <param name="isClosed"></param>
        public bool TryCheckConnectionClosed(out bool isClosed) {
            isClosed = _connectionClosed;
            return true;
        }

        /// <summary>
        /// Closes the accepted connection gracefully.
        /// </summary>
        /// <param name="cancellationToken"></param>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Aborts the accepted connection. When reset is true, implementations should use an abortive close if supported.
        /// </summary>
        /// <param name="reset"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            Dispose();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Gets an optional feature exposed by this engine.
        /// </summary>
        /// <typeparam name="TFeature"></typeparam>
        /// <returns></returns>
        public TFeature? GetFeature<TFeature>() where TFeature : class {
            if (typeof(TFeature) == typeof(ISimpleWTransportDeferredFlushFeature)) {
                return this as TFeature;
            }
            return null;
        }

        /// <summary>
        /// Releases resources.
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            _ = Shutdown(_connection.ClientFd, 2); // default ShutBoth = 2
        }

        /// <summary>
        /// Shutdown
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="how"></param>
        /// <returns></returns>
        [DllImport("libc", EntryPoint = "shutdown", SetLastError = true)]
        private static extern int Shutdown(int socket, int how);

        #endregion ISimpleWEngine

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="localEndPoint"></param>
        /// <param name="deferFlush"></param>
        public IoxideTransport(TcpConnection connection, EndPoint localEndPoint, bool deferFlush) {
            _connection = connection;
            _localEndPoint = localEndPoint;
            _deferFlush = deferFlush;
            _onRecvReady = OnRecvReady;
        }

        #region ISimpleWTransportInput

        /// <summary>
        /// Reads or returns currently buffered bytes.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            CommitAdvance();

            if (_heldBytes > _examinedBytes || _connectionClosed) {
                return new ValueTask<SimpleWTransportReadResult>(BuildReadResult());
            }

            while (true) {
                ValueTask<RecvSnapshot> read = _connection.ReadAsync();
                if (!read.IsCompletedSuccessfully) {
                    _readSignal.Reset();
                    _pendingRead = read.GetAwaiter();
                    _pendingRead.UnsafeOnCompleted(_onRecvReady);
                    return new ValueTask<SimpleWTransportReadResult>(this, _readSignal.Version);
                }

                if (Ingest(read.Result) || _connectionClosed) {
                    return new ValueTask<SimpleWTransportReadResult>(BuildReadResult());
                }
            }
        }

        /// <summary>
        /// Advances the input by the number of consumed and examined bytes from the last buffer.
        /// </summary>
        /// <param name="consumedBytes"></param>
        /// <param name="examinedBytes"></param>
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

        #region read operations

        private readonly Stack<Slice> _slicePool = new();

        private Slice? _head;
        private Slice? _tail;

        private readonly Action _onRecvReady;

        private int _headConsumed;
        private long _heldBytes;
        private long _examinedBytes;
        private ReadOnlySequence<byte> _buffer;
        private long _advanceConsumed;
        private long _advanceExamined;
        private bool _hasRead;

        private ValueTaskAwaiter<RecvSnapshot> _pendingRead;

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

                _readSignal.SetResult(BuildReadResult());
            }
            catch (Exception ex) {
                _readSignal.SetException(ex);
            }
        }

        private bool Ingest(in RecvSnapshot snapshot) {
            bool added = false;

            while (_connection.TryGetItem(snapshot, out SpscRecvRing.Item item)) {
                if (!item.HasBuffer) {
                    continue;
                }
                if (item.Len <= 0) {
                    _connection.ReturnBuffer(in item);
                    continue;
                }

                Slice slice = _slicePool.TryPop(out Slice? pooled) ? pooled : new Slice();
                slice.Set(in item);

                if (_tail == null) {
                    _head = _tail = slice;
                    _headConsumed = 0;
                }
                else {
                    _tail.Link(slice);
                    _tail = slice;
                }

                _heldBytes += item.Len;
                added = true;
            }

            _connectionClosed |= snapshot.IsClosed;
            if (!_connectionClosed) {
                _connection.ResetRead();
            }
            return added;
        }

        private SimpleWTransportReadResult BuildReadResult() {
            if (_head == null) {
                _buffer = ReadOnlySequence<byte>.Empty;
            }
            else if (ReferenceEquals(_head, _tail)) {
                _buffer = new ReadOnlySequence<byte>(_head.Memory.Slice(_headConsumed));
            }
            else {
                _buffer = new ReadOnlySequence<byte>(_head, _headConsumed, _tail!, _tail!.Memory.Length);
            }
            _advanceConsumed = 0;
            _advanceExamined = 0;
            _hasRead = true;
            return new SimpleWTransportReadResult(_buffer, _connectionClosed);
        }

        private void CommitAdvance() {
            if (!_hasRead) {
                return;
            }

            long consumed = Clamp(_advanceConsumed, _heldBytes);
            long examined = Clamp(Math.Max(_advanceExamined, consumed), _heldBytes);
            long remaining = consumed;

            while (remaining > 0 && _head != null) {
                int available = _head.Memory.Length - _headConsumed;
                if (remaining < available) {
                    _headConsumed += (int)remaining;
                    remaining = 0;
                    break;
                }

                Slice released = _head;
                _head = released.NextSlice;
                if (_head == null) {
                    _tail = null;
                }
                _headConsumed = 0;
                remaining -= available;
                _connection.ReturnBuffer(in released.Item);
                released.Release();
                _slicePool.Push(released);
            }

            _heldBytes -= consumed;
            _examinedBytes = Math.Min(examined - consumed, _heldBytes);
            _buffer = ReadOnlySequence<byte>.Empty;
            _advanceConsumed = 0;
            _advanceExamined = 0;
            _hasRead = false;
        }

        #endregion read operations

        #endregion ISimpleWTransportInput

        #region ISimpleWTransportOutput

        /// <summary>
        /// Writes all bytes to the transport.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
        /// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
        public ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            try {
                _connection.Write(buffer.Span);
                return FinishWrite(buffer.Length);
            }
            catch {
                ExitWrite();
                throw;
            }
        }

        /// <summary>
        /// Writes all provided segments to the transport.
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
        /// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
        public ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(segments);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            try {
                long written = 0;
                foreach (ArraySegment<byte> segment in segments) {
                    if (segment.Array == null || segment.Count == 0) {
                        continue;
                    }
                    _connection.Write(segment.AsSpan());
                    written += segment.Count;
                }
                return FinishWrite(written);
            }
            catch {
                ExitWrite();
                throw;
            }
        }

        /// <summary>
        /// Writes both segments to the transport.
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>The number of bytes written to the local transport. Implementations must write the full input or throw.</returns>
        /// <exception cref="InvalidOperationException">A write is already in progress for this output.</exception>
        public ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            EnterWrite();

            try {
                int length = checked(header.Count + body.Count);
                Span<byte> destination = _connection.GetSpan(length);
                if (header.Array != null && header.Count != 0) {
                    header.AsSpan().CopyTo(destination);
                }
                if (body.Array != null && body.Count != 0) {
                    body.AsSpan().CopyTo(destination.Slice(header.Count));
                }
                _connection.Advance(length);
                return FinishWrite(length);
            }
            catch {
                ExitWrite();
                throw;
            }
        }

        #region write operations

        private bool _hasDeferredWrites;
        private int _writing;

        private void EnterWrite() {
            if (Interlocked.Exchange(ref _writing, 1) != 0) {
                throw new InvalidOperationException("Concurrent write on the same ioxide connection.");
            }
        }

        private ValueTask<long> FinishWrite(long written) {
            if (_deferFlush) {
                _hasDeferredWrites |= written != 0;
                ExitWrite();
                return new ValueTask<long>(written);
            }

            ExitWrite();
            ValueTask flush = _connection.FlushAsync();
            if (flush.IsCompletedSuccessfully) {
                flush.GetAwaiter().GetResult();
                return new ValueTask<long>(written);
            }
            return AwaitFlushAsync(flush, written);
        }

        private async ValueTask<long> AwaitFlushAsync(ValueTask flush, long written) {
            await flush;
            return written;
        }

        private void ExitWrite() => Volatile.Write(ref _writing, 0);

        /// <summary>
        /// Completes shutdown and releases all buffers retained by the connection.
        /// </summary>
        public void Complete() {
            if (Interlocked.Exchange(ref _completed, 1) != 0) {
                return;
            }

            try {
                CommitAdvance();
            }
            catch {
            }

            while (_head != null) {
                Slice released = _head;
                _head = released.NextSlice;
                try {
                    _connection.ReturnBuffer(in released.Item);
                }
                catch {
                }
                released.Release();
            }

            _tail = null;
            _heldBytes = 0;
            _examinedBytes = 0;
        }

        private void ThrowIfDisposed() {
            if (Volatile.Read(ref _disposed) != 0) {
                throw new ObjectDisposedException(nameof(IoxideTransport));
            }
        }

        #endregion write operations

        #endregion ISimpleWTransportOutput

        #region ISimpleWTransportDeferredFlushFeature

        private readonly bool _deferFlush;

        /// <summary>
        /// Flushes bytes staged by previous WriteAsync calls.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask FlushDeferredAsync(CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_deferFlush || !_hasDeferredWrites) {
                return ValueTask.CompletedTask;
            }

            ThrowIfDisposed();
            _hasDeferredWrites = false;
            return _connection.FlushAsync();
        }

        #endregion ISimpleWTransportDeferredFlushFeature

        #region SimpleWTransportReadResult

        private ManualResetValueTaskSourceCore<SimpleWTransportReadResult> _readSignal = new() { RunContinuationsAsynchronously = false };

        SimpleWTransportReadResult IValueTaskSource<SimpleWTransportReadResult>.GetResult(short token) {
            return _readSignal.GetResult(token);
        }

        ValueTaskSourceStatus IValueTaskSource<SimpleWTransportReadResult>.GetStatus(short token) {
            return _readSignal.GetStatus(token);
        }

        void IValueTaskSource<SimpleWTransportReadResult>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) {
            _readSignal.OnCompleted(
                continuation,
                state,
                token,
                flags & ~ValueTaskSourceOnCompletedFlags.UseSchedulingContext
            );
        }

        #endregion SimpleWTransportReadResult

        #region bytes operations

        private static long Clamp(long value, long maximum) {
            if (value <= 0) {
                return 0;
            }
            return value >= maximum ? maximum : value;
        }

        private sealed unsafe class NativeMemoryManager : MemoryManager<byte> {

            private byte* _pointer;
            private int _length;

            public void Reset(byte* pointer, int length) {
                _pointer = pointer;
                _length = length;
            }

            public override Span<byte> GetSpan() => new(_pointer, _length);

            public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

            public override void Unpin() {
            }

            protected override void Dispose(bool disposing) {
            }

        }

        private sealed unsafe class Slice : ReadOnlySequenceSegment<byte> {

            private readonly NativeMemoryManager _manager = new();

            public SpscRecvRing.Item Item;
            public Slice? NextSlice;

            public void Set(in SpscRecvRing.Item item) {
                Item = item;
                _manager.Reset(item.Ptr, item.Len);
                Memory = _manager.Memory;
                RunningIndex = 0;
                Next = null;
                NextSlice = null;
            }

            public void Link(Slice next) {
                next.RunningIndex = RunningIndex + Memory.Length;
                Next = next;
                NextSlice = next;
            }

            public void Release() {
                Memory = default;
                Next = null;
                NextSlice = null;
            }

        }

        #endregion bytes operations

    }

}
