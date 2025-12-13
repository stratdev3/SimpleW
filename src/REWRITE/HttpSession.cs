using System.Buffers;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;


namespace SimpleW {

    /// <summary>
    /// HTTP session is used to receive/send HTTP requests/responses from the connected HTTP client.
    /// </summary>
    public sealed class HttpSession : IDisposable {

        /// <summary>
        /// Buffer Size
        /// </summary>
        private const int BufferSize = 16 * 1024;

        /// <summary>
        /// Guid of the current HttpSession
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Underlying SimpleW Server
        /// </summary>
        private readonly SimpleW Server;

        /// <summary>
        /// Socket from SocketAsyncEventArgs
        /// </summary>
        private readonly Socket _socket;

        /// <summary>
        /// ArrayPool Buffer
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// Router
        /// </summary>
        private readonly Router _router;

        /// <summary>
        /// Flag to avoid multiple Connect() call
        /// </summary>
        private bool _receiving;

        /// <summary>
        /// Received Buffer
        /// </summary>
        private byte[] _recvBuffer;

        /// <summary>
        /// Parse Buffer
        /// </summary>
        private byte[] _parseBuffer;

        /// <summary>
        /// Count number of byte in _parseBuffer
        /// </summary>
        private int _parseBufferCount;

        /// <summary>
        /// Parser réutilisé pour cette session
        /// </summary>
        private HttpRequestParserState _parser;

        /// <summary>
        /// Last HttpRequest Parsed
        /// </summary>
        public HttpRequest Request => _request;

        /// <summary>
        /// Last HttpRequest Parsed
        /// </summary>
        private readonly HttpRequest _request;

        /// <summary>
        /// Current HttpResponse
        /// </summary>
        public HttpResponse Response => _response;
        private readonly HttpResponse _response;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        /// <param name="router"></param>
        public HttpSession(SimpleW server, Socket socket, ArrayPool<byte> bufferPool, Router router) {
            Id = Guid.NewGuid();

            Server = server;
            _socket = socket;
            _bufferPool = bufferPool;
            _router = router;

            _recvBuffer = _bufferPool.Rent(BufferSize);
            _parseBuffer = _bufferPool.Rent(BufferSize);
            _parseBufferCount = 0;

            _parser = new HttpRequestParserState(server.MaxRequestHeaderSize, server.MaxRequestBodySize);
            _request = new HttpRequest();

            _response = new HttpResponse(this, _bufferPool);

            SocketOptions();

            Server.MarkSession(this);
        }

        #region Connect

        /// <summary>
        /// Connect
        /// </summary>
        public void Connect() {
            if (_receiving) {
                return;
            }
            _receiving = true;
        }

        /// <summary>
        /// Configure Socket Options
        /// </summary>
        private void SocketOptions() {
            _socket.LingerState = new LingerOption(enable: false, seconds: 0);

            if (Server.OptionKeepAlive) {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            if (Server.OptionTcpKeepAliveTime >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, Server.OptionTcpKeepAliveTime);
            }
            if (Server.OptionTcpKeepAliveInterval >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Server.OptionTcpKeepAliveInterval);
            }
            if (Server.OptionTcpKeepAliveRetryCount >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Server.OptionTcpKeepAliveRetryCount);
            }
            if (Server.OptionNoDelay) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            }

        }

        #endregion Connect

        #region ssl

        /// <summary>
        /// Flag true if current Session is Http
        /// </summary>
        private bool IsSsl => _sslStream != null;

        /// <summary>
        /// SslStream
        /// </summary>
        private SslStream? _sslStream;

        /// <summary>
        /// Add SslContext
        /// </summary>
        /// <param name="sslContext"></param>
        /// <returns></returns>
        public async Task UseHttps(SslContext sslContext) {
            if (IsSsl) {
                return;
            }

            // raw stream on the socket (do not close the socket when we dispose the stream)
            NetworkStream networkStream = new(_socket, ownsSocket: false);

            // SslStream above NetworkStream
            SslStream sslStream = new(
                innerStream: networkStream,
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: sslContext.ClientCertificateValidation
            );

            // server side TLS Handshake
            await sslStream.AuthenticateAsServerAsync(
                serverCertificate: sslContext.Certificate,
                clientCertificateRequired: sslContext.ClientCertificateRequired,
                enabledSslProtocols: sslContext.Protocols,
                checkCertificateRevocation: sslContext.CheckCertificateRevocation
            ).ConfigureAwait(false);

            _sslStream = sslStream;
        }

        #endregion ssl

        #region handle idle/read timeouts

        /// <summary>
        /// Flag LastActivityTick
        /// </summary>
        public long LastActivityTick { get; private set; }

        /// <summary>
        /// Update LastActivityTick
        /// </summary>
        public void MarkActivity() {
            LastActivityTick = Environment.TickCount64;
        }

        #endregion handle idle/read timeouts

        #region Process

        /// <summary>
        /// Flag to close Connection after Response
        /// </summary>
        public bool CloseAfterResponse { get; private set; }

        /// <summary>
        /// Main Process Loop :
        ///  - read from Socket or SslStream to _recvBuffer
        ///  - BlockCopy in _parseBuffer
        ///  - Parse with HttpRequestParserState
        ///  - HttpRouter Dispatch
        /// </summary>
        public async Task ProcessAsync() {
            //
            // MAIN PROCESS LOOP
            //
            while (true) {
                int bytesRead;

                try {
                    if (IsSsl) {
                        bytesRead = await _sslStream!.ReadAsync(_recvBuffer, 0, _recvBuffer.Length).ConfigureAwait(false);
                    }
                    else {
                        bytesRead = await _socket.ReceiveAsync(_recvBuffer.AsMemory(0, _recvBuffer.Length), SocketFlags.None).ConfigureAwait(false);
                    }
                }
                catch (IOException ex)
                        when (ex.InnerException is SocketException se
                              && (se.SocketErrorCode == SocketError.ConnectionReset
                                  || se.SocketErrorCode == SocketError.ConnectionAborted)
                    ) {
                    // client a reset/abort (consider as a normal closed)
                    return;
                }
                catch (SocketException se)
                    when (se.SocketErrorCode == SocketError.ConnectionReset
                          || se.SocketErrorCode == SocketError.ConnectionAborted
                ) {
                    return;
                }
                catch (ObjectDisposedException) {
                    // socket/stream closed (idle timeout or Dispose)
                    return;
                }

                if (bytesRead == 0) {
                    // remote closed
                    return;
                }

                // reset idle timer
                Server.MarkSession(this);

                // byte operations
                EnsureParseBufferCapacity(bytesRead);
                Buffer.BlockCopy(_recvBuffer, 0, _parseBuffer, _parseBufferCount, bytesRead);
                _parseBufferCount += bytesRead;

                try {
                    int offset = 0;

                    // parse HttpRequest (http pipelining support)
                    while (true) {
                        int consumed = _parser.TryReadHttpRequestFast(_parseBuffer, offset, _parseBufferCount - offset, _request);
                        if (consumed == 0) {
                            // if nothing consumed, we need/wait for more data
                            break;
                        }

                        offset += consumed;

                        try {
                            // reset response
                            _response.Reset();

                            // should close connection
                            CloseAfterResponse = ShouldCloseConnection(_request);

                            // router and dispatch
                            await _router.DispatchAsync(this).ConfigureAwait(false);
                            //await SendJsonAsync(new { message = "Hello World !" });

                            // if so, then close connection
                            if (CloseAfterResponse) {
                                return;
                            }
                        }
                        finally {
                            _request.ReturnPooledBodyBuffer();
                        }
                    }

                    // compress buffer
                    if (offset > 0) {
                        int remaining = _parseBufferCount - offset;
                        if (remaining > 0) {
                            Buffer.BlockCopy(_parseBuffer, offset, _parseBuffer, 0, remaining);
                        }
                        _parseBufferCount = remaining;
                    }
                }
                catch (HttpRequestTooLargeException) {
                    CloseAfterResponse = true;
                    await _response.Status(413).Text("Payload Too Large").SendAsync().ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) {
                    Console.WriteLine($"[HTTP] Error while processing {_request?.Method} {_request?.Path} for host '{_request?.Headers.Host ?? "<no-host>"}': {ex}");
                    await _response.Status(500).Text("Internal Server Error").SendAsync().ConfigureAwait(false);
                    return;
                }
            }
        }

        /// <summary>
        /// Update the _closeAfterResponse flag
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private static bool ShouldCloseConnection(HttpRequest request) {

            // HTTP/1.1 : keep-alive by default, except when "Connection: close"
            if (request.Protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase)) {
                return string.Equals(request.Headers.Connection, "close", StringComparison.OrdinalIgnoreCase);
            }

            // HTTP/1.0 : close by default, except when "Connection: keep-alive"
            if (request.Protocol.Equals("HTTP/1.0", StringComparison.OrdinalIgnoreCase) && request.Headers.Connection != null) {
                return !(request.Headers.Connection.IndexOf("keep-alive", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // true to close
            return true;
        }

        /// <summary>
        /// Enlarge Parse Buffer if needed
        /// </summary>
        /// <param name="additionalBytes">number of bytes to add</param>
        private void EnsureParseBufferCapacity(int additionalBytes) {
            int required = _parseBufferCount + additionalBytes;
            if (_parseBuffer.Length >= required) {
                return;
            }

            int newSize = _parseBuffer.Length * 2;
            if (newSize < required) {
                newSize = required;
            }

            byte[] newBuffer = _bufferPool.Rent(newSize);
            Buffer.BlockCopy(_parseBuffer, 0, newBuffer, 0, _parseBufferCount);
            _bufferPool.Return(_parseBuffer);
            _parseBuffer = newBuffer;
        }

        #endregion Process

        #region SendAsync

        /// <summary>
        /// State Gate for SendAsync thread-safe
        /// </summary>
        private int _sending;

        /// <summary>
        /// Storage Segment Array for SendAsync(header, body)
        /// </summary>
        private readonly ArraySegment<byte>[] _sendSegments2 = new ArraySegment<byte>[2];

        /// <summary>
        /// SendAsync to socket (thread safe)
        /// Lower level of sending
        /// </summary>
        /// <param name="segments"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte>[] segments) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream is not null) {
                    // HTTPS : write each segment to sslStream
                    foreach (ArraySegment<byte> seg in segments) {
                        if (seg.Array is null || seg.Count == 0) {
                            continue;
                        }
                        await _sslStream.WriteAsync(seg.Array, seg.Offset, seg.Count).ConfigureAwait(false);
                    }
                }
                else {
                    await _socket.SendAsync(segments, SocketFlags.None).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException) {
            }
            catch (SocketException) {
            }
            finally {
                Volatile.Write(ref _sending, 0);
            }
        }

        /// <summary>
        /// SendAsync to socket (thread safe)
        /// Lower level of sending
        /// </summary>
        /// <param name="header"></param>
        /// <param name="body"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ArraySegment<byte> header, ArraySegment<byte> body) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream is not null) {
                    // HTTPS : write each segment to sslStream
                    if (header.Array is not null && header.Count > 0) {
                        await _sslStream.WriteAsync(header.Array, header.Offset, header.Count).ConfigureAwait(false);
                    }
                    if (body.Array is not null && body.Count > 0) {
                        await _sslStream.WriteAsync(body.Array, body.Offset, body.Count).ConfigureAwait(false);
                    }
                }
                else {
                    _sendSegments2[0] = header;
                    _sendSegments2[1] = body;
                    await _socket.SendAsync(_sendSegments2, SocketFlags.None).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException) {
            }
            catch (SocketException) {
            }
            finally {
                Volatile.Write(ref _sending, 0);
            }
        }

        #endregion SendAsync

        #region IDisposable

        /// <summary>
        /// Flag to avoid multiple Dispose() called
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;

            try {
                if (_socket.Connected) {
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch { }

            try { _socket.Dispose(); } catch { }

            try { _sslStream?.Dispose(); } catch { }
            _sslStream = null;

            if (_recvBuffer is not null) {
                _bufferPool.Return(_recvBuffer);
                _recvBuffer = null!;
            }

            if (_parseBuffer is not null) {
                _bufferPool.Return(_parseBuffer);
                _parseBuffer = null!;
                _parseBufferCount = 0;
            }

        }

        #endregion IDisposable

        #region helper

        /// <summary>
        /// Fake HttpRequest
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        public static bool FakeHttpRequest(ref ReadOnlySequence<byte> buffer, HttpRequest request) {
            request.Reset();

            if (buffer.Length == 0) {
                return false;
            }

            request.Method = "GET";
            request.Path = "/api/test/hello";
            request.Protocol = "HTTP/1.1";
            request.Headers = default;

            buffer = buffer.Slice(buffer.End);
            return true;
        }

        #endregion helper

    }

    /// <summary>
    /// HttpRequest Exception
    /// </summary>
    public sealed class HttpRequestTooLargeException(string message) : Exception(message) {
    }

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
