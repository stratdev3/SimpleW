using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;


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
        private readonly HttpRouter _router;

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
        /// Pipe from ReceiveLoop and NetworkStream Strategy
        /// </summary>
        private Pipe _pipe;

        /// <summary>
        /// Last HttpRequest Parsed
        /// </summary>
        private readonly HttpRequest _request;

        /// <summary>
        /// Parser réutilisé pour cette session
        /// </summary>
        private HttpRequestParserState _parser;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="bufferPool"></param>
        /// <param name="router"></param>
        public HttpSession(SimpleW server, Socket socket, ArrayPool<byte> bufferPool, HttpRouter router) {
            Id = Guid.NewGuid();

            Server = server;
            _socket = socket;
            _bufferPool = bufferPool;
            _router = router;

            _request = new HttpRequest();
            _parser = new HttpRequestParserState(server.MaxRequestHeaderSize, server.MaxRequestBodySize);

            SocketOptions();

            Server.MarkSession(this);
        }

        #region Connect

        /// <summary>
        /// Flag to avoid multiple Connect() call
        /// </summary>
        private bool _receiving;

        /// <summary>
        /// Receive Strategy
        /// </summary>
        private ReceivedStrategy _receivedStrategy;

        /// <summary>
        /// Connect with a received strategy
        /// </summary>
        public void Connect(ReceivedStrategy rs) {
            if (_receiving) {
                return;
            }
            _receiving = true;
            _receivedStrategy = rs;

            if (_receivedStrategy == ReceivedStrategy.SocketEventArgs) {
                _pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
                ReceiveEventArgs();
            }
            else if (_receivedStrategy == ReceivedStrategy.ReceiveLoop) {
                _pipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
                _ = ReceiveLoopAsync();
            }
            else if (_receivedStrategy == ReceivedStrategy.NetworkStream) {
                ReceiveNetworkStream();
            }
            else if (_receivedStrategy == ReceivedStrategy.ReceiveLoopBuffer) {
                _recvBuffer = _bufferPool.Rent(BufferSize);
                _parseBuffer = _bufferPool.Rent(BufferSize);
                _parseBufferCount = 0;
            }
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
        /// NetworkStream or SslStream
        /// </summary>
        private Stream? _transportStream;

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
            _transportStream = sslStream;
        }

        #endregion ssl

        #region receive Strategy NetworkStream

        /// <summary>
        /// Reader for NetworkStream
        /// </summary>
        private PipeReader? _reader;

        /// <summary>
        /// Receive from NetworkStream
        /// </summary>
        private void ReceiveNetworkStream() {
            if (_transportStream is null) {
                _transportStream = new NetworkStream(_socket, ownsSocket: false);
            }

            _reader = PipeReader.Create(
                _transportStream,
                new StreamPipeReaderOptions(
                    pool: MemoryPool<byte>.Shared,
                    bufferSize: BufferSize,
                    minimumReadSize: BufferSize,
                    leaveOpen: true
                )
            );
        }

        #endregion receive Strategy NetworkStream

        #region receive Strategy ReceiveLoop

        /// <summary>
        /// Copy ReceiveAsync to Pipe Writer
        /// </summary>
        /// <returns></returns>
        private async Task ReceiveLoopAsync() {
            PipeWriter writer = _pipe.Writer;

            try {
                while (true) {

                    // 1. request a internal buffer to pipe
                    Memory<byte> memory = writer.GetMemory(BufferSize);

                    int bytesRead;
                    try {
                        if (_sslStream != null) {
                            bytesRead = await _sslStream.ReadAsync(memory).ConfigureAwait(false);
                        }
                        else {
                            bytesRead = await _socket.ReceiveAsync(memory, SocketFlags.None).ConfigureAwait(false);
                        }
                    }
                    catch (ObjectDisposedException) {
                        break;
                    }
                    catch (SocketException) {
                        break;
                    }
                    catch (IOException ex) when (ex.InnerException is SocketException) {
                        break; // ssl exception
                    }

                    if (bytesRead == 0) {
                        // remote closed
                        break;
                    }

                    // 2. update the pipe how many bytes has been read
                    writer.Advance(bytesRead);

                    // 3. reset idle timer
                    Server.MarkSession(this);

                    // 4. flush to the reader
                    FlushResult result = await writer.FlushAsync().ConfigureAwait(false);

                    if (result.IsCompleted) {
                        break;
                    }
                }
            }
            finally {
                await writer.CompleteAsync().ConfigureAwait(false);
            }
        }

        #endregion receive Strategy ReceiveLoop

        #region receive Strategy SocketEventArgs

        private byte[]? _receiveBuffer;
        private SocketAsyncEventArgs _receiveEventArg = null!;

        private void ReceiveEventArgs() {

            // buffer for socket
            _receiveBuffer = _bufferPool.Rent(BufferSize);

            // receive event arg
            _receiveEventArg = new();
            _receiveEventArg.UserToken = this;
            _receiveEventArg.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveEventArg.Completed += OnReceiveAsyncCompleted;

            TryReceive();
        }

        /// <summary>
        /// Receive
        /// </summary>
        private void TryReceive() {
            if (!_socket.Connected) {
                return;
            }

            bool isAsyncOperation;
            try {
                isAsyncOperation = _socket.ReceiveAsync(_receiveEventArg);
            }
            catch (ObjectDisposedException) {
                return;
            }

            // complete (sync)
            if (!isAsyncOperation) {
                ProcessReceive(_receiveEventArg);
            }
            // else it will call OnReceiveAsyncCompleted
        }

        private void ProcessReceive(SocketAsyncEventArgs e) {
            // any error or no more byte, then close the writer pipe
            if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0) {
                _pipe.Writer.Complete();
                return;
            }

            // reset idle timer
            Server.MarkSession(this);

            // copy to pipe
            ReadOnlySpan<byte> span = new(_receiveBuffer, e.Offset, e.BytesTransferred);
            _pipe.Writer.Write(span);

            _ = ContinueReceiveAsync();
        }

        private async Task ContinueReceiveAsync() {
            try {
                FlushResult result = await _pipe.Writer.FlushAsync().ConfigureAwait(false);

                if (result.IsCompleted) {
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                    return;
                }

                TryReceive();
            }
            catch (Exception ex) {
                Console.WriteLine($"[HTTP] ContinueReceiveAsync error: {ex.Message}");
                await _pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
                try { _socket.Close(); }
                catch { }
            }
        }

        /// <summary>
        /// Receive Completed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void OnReceiveAsyncCompleted(object? sender, SocketAsyncEventArgs e) {
            HttpSession? conn = (HttpSession)e.UserToken!;
            conn.ProcessReceive(e);
        }

        #endregion receive Strategy SocketEventArgs

        #region Strategy ReceiveLoopBuffer

        /// <summary>
        /// Main Process Loop :
        ///  - read from Socket or SslStream to _recvBuffer
        ///  - BlockCopy in _parseBuffer
        ///  - Parse with HttpRequestParserState
        ///  - HttpRouter Dispatch
        /// </summary>
        public async Task ReceiveLoopBufferAsync() {
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
                ReadOnlySequence<byte> sequence = new(_parseBuffer, 0, _parseBufferCount);

                try {
                    // parse HttpRequest (http pipelining support)
                    while (
                        _parser.TryReadHttpRequest(ref sequence, _request)
                        //FakeHttpRequest(ref buffer, _request)
                    ) {
                        try {
                            // should close connection
                            _closeAfterResponse = ShouldCloseConnection(_request);

                            // router and dispatch
                            await _router.DispatchAsync(this, _request).ConfigureAwait(false);
                            //await SendJsonAsync(new { message = "Hello World !" });

                            // if so, then close connection
                            if (_closeAfterResponse) {
                                return;
                            }
                        }
                        finally {
                            _request.ReturnPooledBodyBuffer();
                        }
                    }
                }
                catch (HttpRequestTooLargeException) {
                    _closeAfterResponse = true;
                    await SendTextAsync("Payload Too Large", 413, "Payload Too Large").ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) {
                    Console.WriteLine($"[HTTP] Error while processing {_request?.Method} {_request?.Path} for host '{_request?.Headers.Host ?? "<no-host>"}': {ex}");
                    await SendTextAsync("Internal Server Error", 500, "Internal Server Error").ConfigureAwait(false);
                    return;
                }

                // how many consumed the parser
                int remaining = (int)sequence.Length;
                int consumed = _parseBufferCount - remaining;

                if (consumed > 0) {
                    if (remaining > 0) {
                        Buffer.BlockCopy(_parseBuffer, consumed, _parseBuffer, 0, remaining);
                    }
                    _parseBufferCount = remaining;
                }
                // if nothing consumed, we need/wait for more data
            }
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

        #endregion Strategy ReceiveLoopBuffer

        #region Process

        /// <summary>
        /// Main Process Loop :
        ///     1. read pipe
        ///     2. call router
        ///     3. send response
        /// </summary>
        public async Task ProcessAsync() {

            if (_receivedStrategy == ReceivedStrategy.ReceiveLoopBuffer) {
                await ReceiveLoopBufferAsync().ConfigureAwait(false);
                return;
            }

            PipeReader reader = _receivedStrategy == ReceivedStrategy.NetworkStream
                                    ? _reader ?? throw new InvalidOperationException("NetworkStream strategy selected but _reader is null")
                                    : _pipe.Reader;

            try {
                //
                // MAIN PROCESS LOOP
                //
                while (true) {

                    // init reader with idle timeout
                    ReadResult result;
                    try {
                        result = await reader.ReadAsync().ConfigureAwait(false);
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
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    if (_receivedStrategy == ReceivedStrategy.NetworkStream) {
                        // reset idle timer
                        Server.MarkSession(this);
                    }

                    try {
                        // parse HttpRequest (http pipelining support)
                        while (
                            _parser.TryReadHttpRequest(ref buffer, _request)
                            //FakeHttpRequest(ref buffer, _request)
                        ) {
                            try {
                                // should close connection
                                _closeAfterResponse = ShouldCloseConnection(_request);

                                // router and dispatch
                                await _router.DispatchAsync(this, _request).ConfigureAwait(false);
                                //await SendJsonAsync(new { message = "Hello World !" });

                                // if so, then close connection
                                if (_closeAfterResponse) {
                                    reader.AdvanceTo(buffer.Start, buffer.End);
                                    return;
                                }
                            }
                            finally {
                                _request.ReturnPooledBodyBuffer();
                            }
                        }
                    }
                    catch (HttpRequestTooLargeException) {
                        _closeAfterResponse = true;
                        await SendTextAsync("Payload Too Large", 413, "Payload Too Large").ConfigureAwait(false);
                        reader.AdvanceTo(buffer.End);
                        return;
                    }
                    catch (Exception ex) {
                        Console.WriteLine($"[HTTP] Error while processing {_request?.Method} {_request?.Path} for host '{_request?.Headers.Host ?? "<no-host>"}': {ex}");
                        await SendTextAsync("Internal Server Error", 500, "Internal Server Error").ConfigureAwait(false);
                        break; // close connection after server error
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted) {
                        break;
                    }
                }
            }
            finally {
                await reader.CompleteAsync().ConfigureAwait(false);
            }
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

            request.Method = "GET";
            request.Path = "/api/test/hello";
            request.Protocol = "HTTP/1.1";
            request.Headers = default;

            buffer = buffer.Slice(buffer.End);
            return true;
        }

        /// <summary>
        /// Flag to close Connection after Response
        /// </summary>
        private bool _closeAfterResponse;

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

        #region handle idle/read timeouts

        public long LastActivityTick { get; private set; }

        internal void MarkActivity() {
            LastActivityTick = Environment.TickCount64;
        }

        #endregion handle idle/read timeouts

        #endregion Process

        #region SendAsync

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Encoding Ascii = Encoding.ASCII;

        /// <summary>
        /// SendTextAsync
        /// </summary>
        /// <param name="body"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public async ValueTask SendTextAsync(string body, int statusCode = 200, string statusText = "OK", string contentType = "text/plain; charset=utf-8") {
            byte[]? bodyBuffer = null;
            byte[]? headerBuffer = null;

            try {
                //
                // BODY : encode string
                //
                //int bodyByteCount = Encoding.UTF8.GetByteCount(body);
                //bodyBuffer = _bufferPool.Rent(bodyByteCount);
                //int bodyLength = Encoding.UTF8.GetBytes(body, 0, body.Length, bodyBuffer, 0);

                // only one pass on the underlying string (cost: a little extra over allocation by GetMaxByteCount)
                int maxBodyBytes = Utf8NoBom.GetMaxByteCount(body.Length);
                bodyBuffer = _bufferPool.Rent(maxBodyBytes);
                int bodyLength = Utf8NoBom.GetBytes(body.AsSpan(), bodyBuffer.AsSpan());

                //
                // HEADER
                //
                string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                                $"Content-Length: {bodyLength}\r\n" +
                                $"Content-Type: {contentType}\r\n" +
                                $"Connection: {(_closeAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Ascii.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Ascii.GetBytes(header, 0, header.Length, headerBuffer, 0);

                //
                // SEND multi-segments
                //

                await SendSegmentsAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
                                        new ArraySegment<byte>(bodyBuffer, 0, bodyLength))
                      .ConfigureAwait(false);
            }
            finally {
                if (headerBuffer is not null) {
                    _bufferPool.Return(headerBuffer);
                }
                if (bodyBuffer is not null) {
                    _bufferPool.Return(bodyBuffer);
                }
            }
        }

        /// <summary>
        /// SendJsonAsync
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public async ValueTask SendJsonAsync<T>(T value, int statusCode = 200, string statusText = "OK", string contentType = "application/json; charset=utf-8") {
            byte[]? headerBuffer = null;
            PooledBufferWriter? bodyWriter = null;

            try {
                //
                // BODY : serialize json directly into pooled buffer
                //
                bodyWriter = new PooledBufferWriter(_bufferPool);

                Utf8JsonWriter jsonWriter = new(
                    bodyWriter,
                    new JsonWriterOptions {
                        SkipValidation = true, // better perfs is JSON is already valid
                        Indented = false
                    }
                );

                JsonSerializer.Serialize(jsonWriter, value);
                jsonWriter.Flush();

                int bodyLength = bodyWriter.Length;
                byte[] bodyBuffer = bodyWriter.Buffer;

                //
                // HEADER
                //
                string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                                $"Content-Length: {bodyLength}\r\n" +
                                $"Content-Type: {contentType}\r\n" +
                                $"Connection: {(_closeAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Encoding.ASCII.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Encoding.ASCII.GetBytes(header, 0, header.Length, headerBuffer, 0);

                //
                // SEND multi-segments
                //
                await SendSegmentsAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
                                        new ArraySegment<byte>(bodyBuffer, 0, bodyLength))
                      .ConfigureAwait(false);
            }
            finally {
                if (headerBuffer is not null) {
                    _bufferPool.Return(headerBuffer);
                }
                if (bodyWriter is not null) {
                    bodyWriter.Dispose();
                }
            }
        }

        /// <summary>
        /// SendJsonAsync_WithoutBufferWriter
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        [Obsolete("Use SendJsonAsync with PooledBufferWriter instead")]
        public async ValueTask SendJsonAsync_WithoutBufferWriter<T>(T value, int statusCode = 200, string statusText = "OK", string contentType = "application/json; charset=utf-8") {
            byte[]? bodyBuffer = null;
            byte[]? headerBuffer = null;

            try {
                //
                // BODY : encode JSON to UTF-8 bytes
                //
                byte[] rawJson = JsonSerializer.SerializeToUtf8Bytes(value);
                int bodyLength = rawJson.Length;
                bodyBuffer = _bufferPool.Rent(bodyLength);
                Buffer.BlockCopy(rawJson, 0, bodyBuffer, 0, bodyLength);

                //
                // HEADER
                //
                string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                                $"Content-Length: {bodyLength}\r\n" +
                                $"Content-Type: {contentType}\r\n" +
                                $"Connection: {(_closeAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Encoding.ASCII.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Encoding.ASCII.GetBytes(header, 0, header.Length, headerBuffer, 0);

                //
                // SEND multi-segments
                //
                await SendSegmentsAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
                                        new ArraySegment<byte>(bodyBuffer, 0, bodyLength))
                      .ConfigureAwait(false);
            }
            finally {
                if (headerBuffer is not null) {
                    _bufferPool.Return(headerBuffer);
                }
                if (bodyBuffer is not null) {
                    _bufferPool.Return(bodyBuffer);
                }
            }
        }

        /// <summary>
        /// SendAsync
        /// </summary>
        /// <param name="body"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(byte[] body, int statusCode = 200, string statusText = "OK", string contentType = "application/octet-stream") {
            await SendAsync(body.AsMemory(), statusCode, statusText, contentType);
        }

        /// <summary>
        /// SendAsync
        /// </summary>
        /// <param name="body"></param>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public async ValueTask SendAsync(ReadOnlyMemory<byte> body, int statusCode = 200, string statusText = "OK", string contentType = "application/octet-stream") {
            byte[]? headerBuffer = null;

            try {
                //
                // BODY : (pass by argument)
                //
                int bodyLength = body.Length;

                //
                // HEADER
                //
                string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                                $"Content-Length: {bodyLength}\r\n" +
                                $"Content-Type: {contentType}\r\n" +
                                $"Connection: {(_closeAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Encoding.ASCII.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Encoding.ASCII.GetBytes(header.AsSpan(), headerBuffer.AsSpan());

                //
                // SEND multi-segments
                //

                // convert ReadOnlyMemory<byte> into ArraySegment<byte>
                if (MemoryMarshal.TryGetArray(body, out ArraySegment<byte> bodySegment)) {
                    await SendSegmentsAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
                                            bodySegment)
                          .ConfigureAwait(false);
                }
                // fallback (should almost never happens)
                else {
                    await SendSegmentsAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
                                            new ArraySegment<byte>(body.ToArray()))
                          .ConfigureAwait(false);
                }
            }
            finally {
                if (headerBuffer is not null) {
                    _bufferPool.Return(headerBuffer);
                }
            }
        }

        #region lower level Socker SendAsync (thread safe)

        /// <summary>
        /// Semaphore for Socket.SendAsync
        /// </summary>
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        /// <summary>
        /// SendAsync Segments o socket (thread safe)
        /// </summary>
        /// <param name="segments"></param>
        /// <returns></returns>
        private async ValueTask SendSegmentsAsync(ArraySegment<byte>[] segments) {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try {
                if (_sslStream is not null) {
                    // HTTPS : write each segment to sslStream
                    foreach (ArraySegment<byte> seg in segments) {
                        if (seg.Array is null || seg.Count == 0) {
                            continue;
                        }
                        await _sslStream.WriteAsync(seg.Array, seg.Offset, seg.Count).ConfigureAwait(false);
                    }
                    await _sslStream.FlushAsync().ConfigureAwait(false);
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
                _sendLock.Release();
            }
        }

        /// <summary>
        /// SendSegments property
        /// </summary>
        private readonly ArraySegment<byte>[] _sendSegments2 = new ArraySegment<byte>[2];

        /// <summary>
        /// SendAsync Segments to socket (thread safe)
        /// </summary>
        /// <param name="segment1"></param>
        /// <param name="segment2"></param>
        /// <returns></returns>
        private async ValueTask SendSegmentsAsync(ArraySegment<byte> segment1, ArraySegment<byte> segment2) {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try {
                if (_sslStream is not null) {
                    // HTTPS : write each segment to sslStream
                    if (segment1.Array is not null && segment1.Count > 0) {
                        await _sslStream.WriteAsync(segment1.Array, segment1.Offset, segment1.Count).ConfigureAwait(false);
                    }
                    if (segment2.Array is not null && segment2.Count > 0) {
                        await _sslStream.WriteAsync(segment2.Array, segment2.Offset, segment2.Count).ConfigureAwait(false);
                    }
                    await _sslStream.FlushAsync().ConfigureAwait(false);
                }
                else {
                    _sendSegments2[0] = segment1;
                    _sendSegments2[1] = segment2;
                    await _socket.SendAsync(_sendSegments2, SocketFlags.None).ConfigureAwait(false);
                }
            }
            catch (ObjectDisposedException) {
            }
            catch (SocketException) {
            }
            finally {
                _sendLock.Release();
            }
        }

        #endregion lower level Socker SendAsync (thread safe)

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

            if (_receivedStrategy == ReceivedStrategy.ReceiveLoop || _receivedStrategy == ReceivedStrategy.NetworkStream) {
                try {
                    _pipe.Writer.Complete();
                }
                catch { }

                if (_reader is not null) {
                    try {
                        _reader.Complete();
                    }
                    catch { }
                    _reader = null;
                }
            }

            try {
                if (_socket.Connected) {
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch { }

            try { _socket.Dispose(); } catch { }

            if (_receivedStrategy == ReceivedStrategy.SocketEventArgs) {
                if (_receiveEventArg is not null) {
                    _receiveEventArg.Completed -= OnReceiveAsyncCompleted;
                    _receiveEventArg.Dispose();
                }
                if (_receiveBuffer is not null) {
                    _bufferPool.Return(_receiveBuffer);
                    _receiveBuffer = null;
                }
            }
            else if (_receivedStrategy == ReceivedStrategy.ReceiveLoopBuffer) {
                try {
                    if (_recvBuffer is not null) {
                        _bufferPool.Return(_recvBuffer);
                        _recvBuffer = null!;
                    }
                }
                catch { }

                try {
                    if (_parseBuffer is not null) {
                        _bufferPool.Return(_parseBuffer);
                        _parseBuffer = null!;
                        _parseBufferCount = 0;
                    }
                }
                catch { }
            }

            try { _sslStream?.Dispose(); }
            catch { }
            _sslStream = null;

            try { _transportStream?.Dispose(); }
            catch { }
            _transportStream = null;
        }

        #endregion IDisposable

    }

    /// <summary>
    /// Received Strategy
    /// </summary>
    public enum ReceivedStrategy {

        /// <summary>
        /// SocketEventArgs
        /// </summary>
        SocketEventArgs = 1,

        /// <summary>
        /// Receive Loop until received read all data
        /// </summary>
        ReceiveLoop = 2,

        /// <summary>
        /// a NetworkStream handle all read operation other a PipeReader
        /// </summary>
        NetworkStream = 3,

        /// <summary>
        /// Receive Loop with buffer until received read all data
        /// </summary>
        ReceiveLoopBuffer = 4,
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
