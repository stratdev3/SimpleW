using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;


namespace SimpleW {

    /// <summary>
    /// HttpResponse
    /// </summary>
    public sealed class HttpResponse {

        /// <summary>
        /// The Sessions
        /// </summary>
        private readonly HttpSession _session;

        /// <summary>
        /// The ArrayPool
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// UTF8
        /// </summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// ASCII
        /// </summary>
        private static readonly Encoding Ascii = Encoding.ASCII;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="session"></param>
        /// <param name="pool"></param>
        public HttpResponse(HttpSession session, ArrayPool<byte> pool) {
            _session = session;
            _bufferPool = pool;
        }

        /// <summary>
        /// SendTextAsync
        /// </summary>
        /// <param name="body">the string body</param>
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
                                $"Connection: {(_session.CloseAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Ascii.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Ascii.GetBytes(header, 0, header.Length, headerBuffer, 0);

                //
                // SEND multi-segments
                //

                await _session.SendAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
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
        /// <param name="value">the T value</param>
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

                using Utf8JsonWriter jsonWriter = new(
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
                                $"Connection: {(_session.CloseAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Encoding.ASCII.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Encoding.ASCII.GetBytes(header, 0, header.Length, headerBuffer, 0);

                //
                // SEND multi-segments
                //
                await _session.SendAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
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
                                $"Connection: {(_session.CloseAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Encoding.ASCII.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Encoding.ASCII.GetBytes(header, 0, header.Length, headerBuffer, 0);

                //
                // SEND multi-segments
                //
                await _session.SendAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
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
        /// SendAsync alias for body.AsMemory() to ReadOnlyMemory<byte>
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
        /// <param name="body">the ReadOnlyMemory<byte> body</param>
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
                                $"Connection: {(_session.CloseAfterResponse ? "close" : "keep-alive")}\r\n" +
                                "\r\n";

                int headerByteCount = Encoding.ASCII.GetByteCount(header);
                headerBuffer = _bufferPool.Rent(headerByteCount);
                int headerLength = Encoding.ASCII.GetBytes(header.AsSpan(), headerBuffer.AsSpan());

                //
                // SEND multi-segments
                //

                // convert ReadOnlyMemory<byte> into ArraySegment<byte>
                if (MemoryMarshal.TryGetArray(body, out ArraySegment<byte> bodySegment)) {
                    await _session.SendAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
                                                     bodySegment)
                                  .ConfigureAwait(false);
                }
                // fallback (should almost never happens)
                else {
                    await _session.SendAsync(new ArraySegment<byte>(headerBuffer, 0, headerLength),
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

        /// <summary>
        /// Reset
        /// </summary>
        public void Reset() {
            //_pool.Return(_ownedBodyBuffer);
        }
  
    }
}
