using System.Buffers;
using System.Diagnostics;


namespace SimpleW.Buffers {

    /// <summary>
    /// ArrayPool with Reuse Buffer
    /// </summary>
    internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable {

        private readonly ArrayPool<byte> _pool;
        private byte[] _buffer;
        private int _written;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pool"></param>
        /// <param name="initialSize"></param>
        public PooledBufferWriter(ArrayPool<byte> pool, int initialSize = 4096) {
            _pool = pool;
            _buffer = _pool.Rent(initialSize);
            _written = 0;
        }

        public int Length => _written;
        public byte[] Buffer => _buffer;

        public void Advance(int count) {
            Debug.Assert(count >= 0);
            int newSize = _written + count;
            Debug.Assert(newSize <= _buffer.Length);
            _written = newSize;
        }

        public Memory<byte> GetMemory(int sizeHint = 0) {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0) {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        private void EnsureCapacity(int sizeHint) {
            if (sizeHint <= 0) {
                sizeHint = 256;
            }

            int available = _buffer.Length - _written;
            if (available >= sizeHint) {
                return;
            }

            // we need to resize
            int newSize = _buffer.Length * 2;
            int required = _written + sizeHint;
            if (newSize < required) {
                newSize = required;
            }

            byte[] newBuffer = _pool.Rent(newSize);
            System.Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _written);
            _pool.Return(_buffer);
            _buffer = newBuffer;
        }

        public void Dispose() {
            if (_buffer != null!) {
                _pool.Return(_buffer);
                _buffer = null!;
                _written = 0;
            }
        }

    }

}
