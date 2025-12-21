using System.Buffers;


namespace SimpleW.Buffers {

    /// <summary>
    /// Stream wrapper writing into an IBufferWriter (no intermediate allocations).
    /// </summary>
    internal sealed class BufferWriterStream : Stream {

        private readonly IBufferWriter<byte> _writer;
        public BufferWriterStream(IBufferWriter<byte> writer) => _writer = writer;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) {
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer) {
            if (buffer.IsEmpty)
                return;
            Span<byte> dst = _writer.GetSpan(buffer.Length);
            buffer.CopyTo(dst);
            _writer.Advance(buffer.Length);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
            Write(buffer.Span);
            return default;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }
    }

}
