using System.Buffers;


namespace SimpleW.Buffers {

    /// <summary>
    /// Stream wrapper writing into an IBufferWriter (no intermediate allocations).
    /// </summary>
    public sealed class BufferWriterStream : Stream {

        /// <summary>
        /// The underlying writer
        /// </summary>
        private readonly IBufferWriter<byte> _writer;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="writer"></param>
        public BufferWriterStream(IBufferWriter<byte> writer) => _writer = writer;

        /// <summary>
        /// Can read
        /// </summary>
        public override bool CanRead => false;

        /// <summary>
        /// Can seek
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Can write
        /// </summary>
        public override bool CanWrite => true;

        /// <summary>
        /// Length
        /// </summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// Seek Position
        /// </summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>
        /// Flush
        /// </summary>
        public override void Flush() { }

        /// <summary>
        /// FlushAsync
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Read
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>
        /// Seek
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="origin"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>
        /// SetLength
        /// </summary>
        /// <param name="value"></param>
        /// <exception cref="NotSupportedException"></exception>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public override void Write(byte[] buffer, int offset, int count) {
            Write(buffer.AsSpan(offset, count));
        }

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="buffer"></param>
        public override void Write(ReadOnlySpan<byte> buffer) {
            if (buffer.IsEmpty)
                return;
            Span<byte> dst = _writer.GetSpan(buffer.Length);
            buffer.CopyTo(dst);
            _writer.Advance(buffer.Length);
        }

        /// <summary>
        /// WriteAsync
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            Write(buffer.Span);
            return default;
        }

        /// <summary>
        /// WriteAsync
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }
    }

}
