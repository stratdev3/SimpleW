using System.Buffers;
using System.Net;
using System.Text;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for transport input buffering.
    /// </summary>
    public class EngineTests {

        [Fact]
        public async Task DelegateTransportInput_Should_Return_Complete_Request() {

            using DelegateTransport transport = CreateTransport(
                "GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"
            );

            SimpleWTransportReadResult result = await transport.Input.ReadAsync();

            Check.That(ToAscii(result.Buffer)).IsEqualTo("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n");
            Check.That(result.IsCompleted).IsFalse();

            transport.Input.AdvanceTo(result.Buffer.Length, result.Buffer.Length);
            SimpleWTransportReadResult completed = await transport.Input.ReadAsync();

            Check.That(completed.Buffer.Length).IsEqualTo(0);
            Check.That(completed.IsCompleted).IsTrue();
        }

        [Fact]
        public async Task DelegateTransportInput_Should_Preserve_Pipelined_Bytes_After_Advance() {

            int readCount = 0;
            using DelegateTransport transport = CreateTransport(
                () => readCount++,
                "GET /one HTTP/1.1\r\nHost: localhost\r\n\r\n" +
                "GET /two HTTP/1.1\r\nHost: localhost\r\n\r\n"
            );

            SimpleWTransportReadResult result = await transport.Input.ReadAsync();
            long firstLength = Encoding.ASCII.GetByteCount("GET /one HTTP/1.1\r\nHost: localhost\r\n\r\n");

            transport.Input.AdvanceTo(firstLength, firstLength);
            SimpleWTransportReadResult remaining = await transport.Input.ReadAsync();

            Check.That(readCount).IsEqualTo(1);
            Check.That(ToAscii(remaining.Buffer)).IsEqualTo("GET /two HTTP/1.1\r\nHost: localhost\r\n\r\n");
        }

        [Fact]
        public async Task DelegateTransportInput_Should_Append_Fragmented_Request_When_Examined() {

            using DelegateTransport transport = CreateTransport(
                "GET /",
                " HTTP/1.1\r\nHost: localhost\r\n\r\n"
            );

            SimpleWTransportReadResult first = await transport.Input.ReadAsync();
            Check.That(ToAscii(first.Buffer)).IsEqualTo("GET /");

            transport.Input.AdvanceTo(0, first.Buffer.Length);
            SimpleWTransportReadResult second = await transport.Input.ReadAsync();

            Check.That(ToAscii(second.Buffer)).IsEqualTo("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n");
        }

        [Fact]
        public async Task DelegateTransportInput_Should_Keep_Partial_Body_Until_Complete() {

            using DelegateTransport transport = CreateTransport(
                "POST / HTTP/1.1\r\nHost: localhost\r\nContent-Length: 5\r\n\r\nhel",
                "lo"
            );

            SimpleWTransportReadResult first = await transport.Input.ReadAsync();
            Check.That(ToAscii(first.Buffer)).EndsWith("hel");

            transport.Input.AdvanceTo(0, first.Buffer.Length);
            SimpleWTransportReadResult second = await transport.Input.ReadAsync();

            Check.That(ToAscii(second.Buffer)).EndsWith("hello");
        }

        [Fact]
        public async Task HttpSession_Should_Parse_MultiSegment_Transport_Buffer() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.MapGet("/", () => "OK");

            ReadOnlySequence<byte> request = CreateSequence(
                "GET / HTTP/1.1\r\nHost: local",
                "host\r\n\r\n"
            );
            var input = new SequenceTransportInput(request);
            using var transport = new SequenceTransport(input);
            using var session = new HttpSession(server, transport, ArrayPool<byte>.Shared);

            await session.ProcessAsync();

            string response = Encoding.ASCII.GetString(transport.WrittenBytes.ToArray());
            Check.That(response).Contains("200 OK");
            Check.That(response).Contains("OK");
            Check.That(input.ConsumedBytes).IsEqualTo(request.Length);
            Check.That(input.ExaminedBytes).IsEqualTo(request.Length);
        }

        [Fact]
        public async Task HttpSession_SendAsync_Should_Fail_Fast_On_Concurrent_Transport_Write_And_Count_Only_Successful_Send() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            TaskCompletionSource<bool> writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int writeCalls = 0;

            using DelegateTransport transport = new(
                (_, _) => new ValueTask<int>(0),
                async (buffer, _) => {
                    if (Interlocked.Increment(ref writeCalls) == 1) {
                        writeEntered.SetResult(true);
                        await releaseWrite.Task.ConfigureAwait(false);
                    }
                    return buffer.Length;
                }
            );
            using var session = new HttpSession(server, transport, ArrayPool<byte>.Shared);

            Task firstSend = session.SendAsync(Encoding.ASCII.GetBytes("hello")).AsTask();
            await writeEntered.Task;

            try {
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(Encoding.ASCII.GetBytes("boom")).AsTask());
                Check.That(session.Response.BytesSent).IsEqualTo(0);
                Check.That(writeCalls).IsEqualTo(1);
            }
            finally {
                releaseWrite.SetResult(true);
            }

            await firstSend;

            Check.That(session.Response.BytesSent).IsEqualTo(5);
            Check.That(writeCalls).IsEqualTo(1);
        }

        [Fact]
        public void HttpSession_Should_Expose_Authenticated_Client_Certificate_Metadata() {
            TestTlsFeature feature = new("http/1.1", "CN=client, O=SimpleW", "client@simplew.test", true);
            using DelegateTransport transport = CreateTlsTransport(feature);
            using var session = new HttpSession(new SimpleWServer(IPAddress.Loopback, 0), transport, ArrayPool<byte>.Shared);

            Assert.Equal("http/1.1", session.NegotiatedApplicationProtocol);
            Assert.Equal("CN=client, O=SimpleW", session.ClientCertificateSubject);
            Assert.Equal("client@simplew.test", session.ClientCertificateEmailAddress);
            Assert.Equal(true, session.IsClientCertificateAuthenticated);
        }

        [Fact]
        public void HttpSession_Should_Expose_Authenticated_Client_Certificate_Without_Email() {
            TestTlsFeature feature = new("http/1.1", "CN=client, O=SimpleW", null, true);
            using DelegateTransport transport = CreateTlsTransport(feature);
            using var session = new HttpSession(new SimpleWServer(IPAddress.Loopback, 0), transport, ArrayPool<byte>.Shared);

            Assert.Equal("CN=client, O=SimpleW", session.ClientCertificateSubject);
            Assert.Null(session.ClientCertificateEmailAddress);
            Assert.Equal(true, session.IsClientCertificateAuthenticated);
        }

        [Fact]
        public void HttpSession_Should_Expose_Absent_Client_Certificate() {
            TestTlsFeature feature = new("http/1.1", null, null, false);
            using DelegateTransport transport = CreateTlsTransport(feature);
            using var session = new HttpSession(new SimpleWServer(IPAddress.Loopback, 0), transport, ArrayPool<byte>.Shared);

            Assert.Equal("http/1.1", session.NegotiatedApplicationProtocol);
            Assert.Null(session.ClientCertificateSubject);
            Assert.Null(session.ClientCertificateEmailAddress);
            Assert.Equal(false, session.IsClientCertificateAuthenticated);
        }

        [Fact]
        public void HttpSession_Should_Expose_Null_Tls_Metadata_When_Feature_Is_Unavailable() {
            using DelegateTransport transport = CreateTlsTransport(null);
            using var session = new HttpSession(new SimpleWServer(IPAddress.Loopback, 0), transport, ArrayPool<byte>.Shared);

            Assert.Null(session.NegotiatedApplicationProtocol);
            Assert.Null(session.ClientCertificateSubject);
            Assert.Null(session.ClientCertificateEmailAddress);
            Assert.Null(session.IsClientCertificateAuthenticated);
        }

        private static DelegateTransport CreateTransport(params string[] chunks)
            => CreateTransport(null, chunks);

        private static DelegateTransport CreateTransport(Action? onRead, params string[] chunks) {
            byte[][] reads = chunks.Select(Encoding.ASCII.GetBytes).ToArray();
            int index = 0;
            return new DelegateTransport(
                (destination, _) => {
                    onRead?.Invoke();
                    if (index >= reads.Length) {
                        return new ValueTask<int>(0);
                    }

                    byte[] source = reads[index++];
                    source.AsSpan().CopyTo(destination.Span);
                    return new ValueTask<int>(source.Length);
                },
                (buffer, _) => new ValueTask<long>(buffer.Length)
            );
        }

        private static DelegateTransport CreateTlsTransport(ISimpleWTransportTlsFeature? feature) {
            return new DelegateTransport(
                (_, _) => new ValueTask<int>(0),
                (buffer, _) => new ValueTask<long>(buffer.Length),
                isEncrypted: true,
                getFeature: type => type == typeof(ISimpleWTransportTlsFeature) ? feature : null
            );
        }

        private static string ToAscii(ReadOnlySequence<byte> buffer)
            => Encoding.ASCII.GetString(buffer.ToArray());

        private static ReadOnlySequence<byte> CreateSequence(params string[] segments) {
            if (segments.Length == 0) {
                return ReadOnlySequence<byte>.Empty;
            }

            BufferSegment first = new(Encoding.ASCII.GetBytes(segments[0]));
            BufferSegment last = first;
            for (int i = 1; i < segments.Length; i++) {
                last = last.Append(Encoding.ASCII.GetBytes(segments[i]));
            }
            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private sealed class BufferSegment : ReadOnlySequenceSegment<byte> {

            public BufferSegment(ReadOnlyMemory<byte> memory) {
                Memory = memory;
            }

            public BufferSegment Append(ReadOnlyMemory<byte> memory) {
                BufferSegment segment = new(memory) {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;
                return segment;
            }

        }

        private sealed class SequenceTransportInput : ISimpleWTransportInput {

            private readonly ReadOnlySequence<byte> _buffer;
            private bool _read;

            public long ConsumedBytes { get; private set; }
            public long ExaminedBytes { get; private set; }

            public SequenceTransportInput(ReadOnlySequence<byte> buffer) {
                _buffer = buffer;
            }

            public ValueTask<SimpleWTransportReadResult> ReadAsync(CancellationToken cancellationToken = default) {
                if (_read) {
                    return new ValueTask<SimpleWTransportReadResult>(new SimpleWTransportReadResult(ReadOnlySequence<byte>.Empty, IsCompleted: true));
                }

                _read = true;
                return new ValueTask<SimpleWTransportReadResult>(new SimpleWTransportReadResult(_buffer, IsCompleted: false));
            }

            public void AdvanceTo(long consumedBytes, long examinedBytes) {
                ConsumedBytes = consumedBytes;
                ExaminedBytes = examinedBytes;
            }

        }

        private sealed class SequenceTransport : ISimpleWEngine, ISimpleWTransportOutput {

            public MemoryStream WrittenBytes { get; } = new();
            public EndPoint? LocalEndPoint => null;
            public EndPoint? RemoteEndPoint => null;
            public bool IsEncrypted => false;
            public ISimpleWTransportInput Input { get; }
            public ISimpleWTransportOutput Output => this;

            public SequenceTransport(ISimpleWTransportInput input) {
                Input = input;
            }

            public bool TryCheckConnectionClosed(out bool isClosed) {
                isClosed = false;
                return false;
            }

            public ValueTask CloseAsync(CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;

            public ValueTask AbortAsync(bool reset = false, CancellationToken cancellationToken = default)
                => ValueTask.CompletedTask;

            public TFeature? GetFeature<TFeature>() where TFeature : class
                => null;

            public ValueTask<long> WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) {
                WrittenBytes.Write(buffer.Span);
                return new ValueTask<long>(buffer.Length);
            }

            public async ValueTask<long> WriteAsync(ArraySegment<byte> header, ArraySegment<byte> body, CancellationToken cancellationToken = default) {
                long bytesWritten = 0;
                if (header.Count > 0) {
                    bytesWritten += await WriteAsync(header, cancellationToken).ConfigureAwait(false);
                }
                if (body.Count > 0) {
                    bytesWritten += await WriteAsync(body, cancellationToken).ConfigureAwait(false);
                }
                return bytesWritten;
            }

            public async ValueTask<long> WriteAsync(ArraySegment<byte>[] segments, CancellationToken cancellationToken = default) {
                long bytesWritten = 0;
                foreach (ArraySegment<byte> segment in segments) {
                    if (segment.Array == null || segment.Count <= 0) {
                        continue;
                    }
                    bytesWritten += await WriteAsync(segment.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                return bytesWritten;
            }

            public void Dispose() {
                WrittenBytes.Dispose();
            }

        }

        private sealed record TestTlsFeature(
            string? NegotiatedApplicationProtocol,
            string? ClientCertificateSubject,
            string? ClientCertificateEmailAddress,
            bool IsClientCertificateAuthenticated
        ) : ISimpleWTransportTlsFeature;

    }

}
