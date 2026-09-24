namespace SimpleW.Service.FileBrowser {

    /// <summary>Enforces extraction quotas before each write; the caller owns the destination.</summary>
    internal sealed class ExtractionOutputStream(Stream destination, long maxFileBytes, long remainingTotalBytes, CancellationToken cancellationToken) : Stream {
        internal long BytesWritten { get; private set; }

        public override void Write(ReadOnlySpan<byte> buffer) {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > maxFileBytes - BytesWritten) {
                throw new InvalidDataException("archive_entry_too_large");
            }
            if (buffer.Length > remainingTotalBytes - BytesWritten) {
                throw new InvalidDataException("archive_too_large");
            }
            destination.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void WriteByte(byte value) {
            Span<byte> buffer = stackalloc byte[1];
            buffer[0] = value;
            Write(buffer);
        }
        public override void Flush() => destination.Flush();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => destination.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
