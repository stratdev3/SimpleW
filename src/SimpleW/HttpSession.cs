using System.Buffers;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using SimpleW.Parsers;


namespace SimpleW {

    /// <summary>
    /// HTTP session is used to receive/send HTTP requests/responses from the connected HTTP client.
    /// </summary>
    public sealed class HttpSession : IDisposable {

        /// <summary>
        /// Guid of the current HttpSession
        /// </summary>
        public Guid Id { get; }

        /// <summary>
        /// Underlying SimpleW Server
        /// </summary>
        public readonly SimpleWServer Server;

        /// <summary>
        /// Expose the Server.JsonEngine
        /// </summary>
        public IJsonEngine JsonEngine => Server.JsonEngine;

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
        private HttpRequestParser _parser;

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

        /// <summary>
        /// Current HttpResponse
        /// </summary>
        private readonly HttpResponse _response;

        /// <summary>
        /// Exposes the underlying transport as a Stream (NetworkStream or SslStream)
        /// NOTE: caller must NOT dispose this stream (it would close the socket)
        /// </summary>
        public Stream TransportStream => (_sslStream != null ? _sslStream : new NetworkStream(_socket, ownsSocket: false));

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="server"></param>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        /// <param name="router"></param>
        public HttpSession(SimpleWServer server, Socket socket, ArrayPool<byte> bufferPool, Router router) {
            Id = Guid.NewGuid();

            Server = server;
            _socket = socket;
            _bufferPool = bufferPool;
            _router = router;

            _recvBuffer = _bufferPool.Rent(server.Options.ReceiveBufferSize);
            _parseBuffer = _bufferPool.Rent(server.Options.ReceiveBufferSize);
            _parseBufferCount = 0;

            _parser = new HttpRequestParser(server.Options.MaxRequestHeaderSize, server.Options.MaxRequestBodySize);
            _request = new HttpRequest();
            _request.ParserSetJsonEngine(server.JsonEngine);

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

            // enable=false : normal "graceful" close par défaut (send FIN and flush buffer)
            // enable=true, seconds=0 : abort and send immediate RST
            // enable=true, seconds>0 : wait flush buffer for X second, abort and send RST
            _socket.LingerState = new LingerOption(enable: false, seconds: 0);

            if (Server.Options.TcpKeepAlive) {
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            if (Server.Options.TcpKeepAliveTime >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, Server.Options.TcpKeepAliveTime);
            }
            if (Server.Options.TcpKeepAliveInterval >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, Server.Options.TcpKeepAliveInterval);
            }
            if (Server.Options.TcpKeepAliveRetryCount >= 0) {
                _socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, Server.Options.TcpKeepAliveRetryCount);
            }
            if (Server.Options.TcpNoDelay) {
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
        /// Flag
        /// </summary>
        private int _isTransportOwned;

        /// <summary>
        /// Is Transport Owner by external entity and not HttpSession anymore
        /// </summary>
        internal bool IsTransportOwned => Volatile.Read(ref _isTransportOwned) == 1;

        /// <summary>
        /// Called to stop HTTP parsing and take over the transport
        /// </summary>
        internal bool TryTakeTransportOwnership() => Interlocked.Exchange(ref _isTransportOwned, 1) == 0;

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
            if (IsTransportOwned) {
                return;
            }

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
                        int consumed = _parser.TryReadHttpRequest(_parseBuffer, offset, _parseBufferCount - offset, _request);
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
                catch (HttpBadRequestException) {
                    CloseAfterResponse = true;
                    await _response.Status(400).Text("Bad Request").SendAsync().ConfigureAwait(false);
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
                return request.Headers.Connection != null && request.Headers.Connection.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            // HTTP/1.0 : close by default, except when "Connection: keep-alive"
            if (request.Protocol.Equals("HTTP/1.0", StringComparison.OrdinalIgnoreCase)) {
                return request.Headers.Connection == null || request.Headers.Connection.IndexOf("keep-alive", StringComparison.OrdinalIgnoreCase) < 0;
            }

            // true to close
            return true;
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
        /// SendAsync native to socket (thread safe)
        /// Lower level of sending
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream is not null) {
                    await _sslStream.WriteAsync(buffer).ConfigureAwait(false);
                }
                else {
                    await _socket.SendAsync(buffer, SocketFlags.None).ConfigureAwait(false);
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

        /// <summary>
        /// SendAsync to socket (thread safe)
        /// Lower level of sending
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask SendAsync(ArraySegment<byte> buffer) {
            try {
                if (Interlocked.Exchange(ref _sending, 1) != 0) {
                    throw new InvalidOperationException("Concurrent SendAsync on same session");
                }
                if (_sslStream is not null) {
                    await _sslStream.WriteAsync(buffer.AsMemory()).ConfigureAwait(false);
                }
                else {
                    await _socket.SendAsync(buffer.AsMemory(), SocketFlags.None).ConfigureAwait(false);
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

            request.ParserSetMethod("GET");
            request.ParserSetPath("/api/test/hello");
            request.ParserSetProtocol("HTTP/1.1");
            request.ParserSetHeaders(default);

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
    /// HttpBadRequest Exception
    /// </summary>
    /// <param name="message"></param>
    public sealed class HttpBadRequestException(string message) : Exception(message) {
    }

}
