using System.Buffers;
using System.Text;


namespace SimpleW.Parsers {

    /// <summary>
    /// HttpRequest Parser Instance
    /// </summary>
    internal struct HttpRequestParser {

        #region Constants & shared fields

        private static readonly byte[] Crlf = { (byte)'\r', (byte)'\n' };
        private static readonly byte[] HeaderTerminator = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

        private const byte SpaceByte = (byte)' ';
        private const byte ColonByte = (byte)':';

        private const string HeaderContentLength = "Content-Length";
        private const string HeaderTransferEncoding = "Transfer-Encoding";

        private static readonly Encoding Ascii = Encoding.ASCII;

        #endregion

        /// <summary>
        /// MaxHeaderSize
        /// </summary>
        private readonly int _maxHeaderSize;

        /// <summary>
        /// MaxBodySize
        /// </summary>
        private readonly long _maxBodySize;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="maxHeaderSize"></param>
        /// <param name="maxBodySize"></param>
        public HttpRequestParser(int maxHeaderSize, long maxBodySize) {
            _maxHeaderSize = maxHeaderSize;
            _maxBodySize = maxBodySize;
        }

        /// <summary>
        /// Parse HttpRequest from contiguous buffer
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="length"></param>
        /// <param name="request"></param>
        /// <returns>
        /// Return number of bytes consumed in buffer[offset..offset+length],
        /// or 0 if need more data to read the request
        /// </returns>
        /// <exception cref="HttpRequestTooLargeException"></exception>
        public int TryReadHttpRequest(byte[] buffer, int offset, int length, HttpRequest request) {
            request.Reset();

            if (length <= 0) {
                return 0;
            }

            ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(buffer, offset, length);

            // 1. find header end (CRLF CRLF)
            int headerEnd = span.IndexOf(HeaderTerminator);
            if (headerEnd < 0) {
                // check for headers max size
                if (span.Length > _maxHeaderSize) {
                    throw new HttpRequestTooLargeException($"Request headers too large: {span.Length} bytes (limit: {_maxHeaderSize}).");
                }
                return 0;
            }

            // check for headers max size
            int headerBytesLen = headerEnd + HeaderTerminator.Length;
            if (headerBytesLen > _maxHeaderSize) {
                throw new HttpRequestTooLargeException($"Request headers too large: {headerBytesLen} bytes (limit: {_maxHeaderSize}).");
            }

            ReadOnlySpan<byte> headerSpan = span.Slice(0, headerEnd + Crlf.Length);

            // 2. request line
            int firstCrlf = headerSpan.IndexOf(Crlf);
            if (firstCrlf <= 0) {
                throw new HttpBadRequestException("Invalid request line.");
            }

            ReadOnlySpan<byte> requestLineSpan = headerSpan.Slice(0, firstCrlf);
            if (!TryParseRequestLine(requestLineSpan, out string method, out string rawTarget, out string path, out string protocol, out string queryString, out ReadOnlySpan<byte> querySpan)) {
                throw new HttpBadRequestException("");
            }

            request.ParserSetMethod(method);
            request.ParserSetRawTarget(rawTarget);
            request.ParserSetPath(path);
            request.ParserSetProtocol(protocol);
            request.ParserSetQueryString(queryString);
            HttpRequestParser.ParseQueryString(querySpan, request.Query);

            // 3. headers
            HttpHeaders headers = default;
            long? contentLength = null;
            bool isChunked = false;

            int headerPos = firstCrlf + Crlf.Length;

            while (headerPos < headerEnd) {
                int rel = headerSpan.Slice(headerPos).IndexOf(Crlf);
                if (rel < 0) {
                    break;
                }

                ReadOnlySpan<byte> lineSpan = headerSpan.Slice(headerPos, rel);
                headerPos += rel + Crlf.Length;

                if (lineSpan.Length == 0) {
                    continue;
                }

                if (!TryParseHeaderLine(lineSpan, out string? name, out string? value) || name is null) {
                    throw new HttpBadRequestException("Invalid header line.");
                }

                value ??= string.Empty;
                headers.Add(name, value);

                if (name.Equals(HeaderContentLength, StringComparison.OrdinalIgnoreCase)) {
                    if (long.TryParse(value, out long parsedCL) && parsedCL >= 0) {
                        contentLength = parsedCL;
                    }
                }
                else if (name.Equals(HeaderTransferEncoding, StringComparison.OrdinalIgnoreCase)) {
                    if (value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) {
                        isChunked = true;
                    }
                }
            }
            //if (request.Protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase) && headers.Host is null) {
            //    throw new HttpBadRequestException("Missing Host header (HTTP/1.1).");
            //}

            request.ParserSetHeaders(headers);

            // no content-length and no chunked
            if (!isChunked && (!contentLength.HasValue || contentLength.Value == 0)) {
                request.ParserSetBody(ReadOnlySequence<byte>.Empty);
                return headerBytesLen;
            }

            // 4. body (Content-Length ou chunked)
            int bodyStart = headerBytesLen;
            int availableBodyBytes = length - bodyStart;

            // chunked
            if (isChunked) {
                if (!TryReadChunkedBody(span, bodyStart, out ReadOnlySequence<byte> bodySeq, out byte[]? pooledBuffer, out int totalConsumed)) {
                    // need more data
                    return 0;
                }

                request.ParserSetBody(bodySeq);
                request.PooledBodyBuffer = pooledBuffer;
                return totalConsumed;
            }

            // check for body max size (hide warning on contextLengh as we already know it has value)
            long clLong = contentLength!.Value;
            if (clLong > _maxBodySize) {
                throw new HttpRequestTooLargeException($"Request body too large: {clLong} bytes (limit: {_maxBodySize}).");
            }

            int bodyLength = checked((int)clLong);

            if (availableBodyBytes < bodyLength) {
                // need more data
                return 0;
            }

            // contiguous body in the same buffer
            int bodyOffsetInBuffer = offset + bodyStart;
            request.ParserSetBody(new ReadOnlySequence<byte>(buffer, bodyOffsetInBuffer, bodyLength));

            // total consumed = headers + body
            return headerBytesLen + bodyLength;
        }

        /// <summary>
        /// Request Line
        /// </summary>
        /// <param name="lineSpan"></param>
        /// <param name="method"></param>
        /// <param name="rawTarget"></param>
        /// <param name="path"></param>
        /// <param name="protocol"></param>
        /// <param name="queryString"></param>
        /// <param name="querySpan"></param>
        /// <returns></returns>
        private static bool TryParseRequestLine(ReadOnlySpan<byte> lineSpan, out string method, out string rawTarget, out string path, out string protocol, out string queryString, out ReadOnlySpan<byte> querySpan) {
            method = rawTarget = protocol = string.Empty;
            path = queryString = string.Empty;
            querySpan = default;

            if (lineSpan.Length == 0) {
                return false;
            }

            int firstSpace = lineSpan.IndexOf(SpaceByte);
            if (firstSpace <= 0) {
                return false;
            }

            int secondSpace = lineSpan.Slice(firstSpace + 1).IndexOf(SpaceByte);
            if (secondSpace < 0) {
                return false;
            }
            secondSpace += firstSpace + 1;

            ReadOnlySpan<byte> methodSpan = lineSpan.Slice(0, firstSpace);
            ReadOnlySpan<byte> targetSpan = lineSpan.Slice(firstSpace + 1, secondSpace - firstSpace - 1);
            ReadOnlySpan<byte> protocolSpan = TrimAsciiWhitespace(lineSpan.Slice(secondSpace + 1));

            if (protocolSpan.Length == 0) {
                throw new HttpBadRequestException("Missing protocol.");
            }

            method = Ascii.GetString(methodSpan);
            protocol = Ascii.GetString(protocolSpan);

            if (targetSpan.Length == 0) {
                throw new HttpBadRequestException("Empty request-target.");
            }
            if (targetSpan[0] != (byte)'/') {
                throw new HttpBadRequestException("Unsupported request-target form.");
            }

            // find '?'
            int qIndex = targetSpan.IndexOf((byte)'?');
            ReadOnlySpan<byte> pathSpan;

            if (qIndex >= 0) {
                pathSpan = targetSpan.Slice(0, qIndex);
                if (qIndex + 1 < targetSpan.Length) {
                    querySpan = targetSpan.Slice(qIndex + 1);
                }
            }
            else {
                pathSpan = targetSpan;
            }

            // convert RawTarget ONLY ONCE (optional, but you probably want it)
            rawTarget = Ascii.GetString(targetSpan);
            // decode path/query *from bytes*
            path = DecodePathBytes(pathSpan);
            // querystring raw (NO leading '?')
            queryString = querySpan.Length == 0 ? string.Empty : Ascii.GetString(querySpan);

            return true;
        }

        /// <summary>
        /// Header
        /// </summary>
        /// <param name="lineSpan"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseHeaderLine(ReadOnlySpan<byte> lineSpan, out string? name, out string? value) {
            name = null;
            value = null;

            if (lineSpan.Length == 0) {
                return false;
            }

            int colonIndex = lineSpan.IndexOf(ColonByte);
            if (colonIndex <= 0) {
                return false;
            }

            ReadOnlySpan<byte> nameSpan = TrimAsciiWhitespace(lineSpan.Slice(0, colonIndex));
            ReadOnlySpan<byte> valueSpan = TrimAsciiWhitespace(lineSpan.Slice(colonIndex + 1));

            if (nameSpan.Length == 0) {
                return false;
            }

            name = Ascii.GetString(nameSpan);
            value = valueSpan.Length > 0 ? Ascii.GetString(valueSpan) : string.Empty;
            return true;
        }

        /// <summary>
        /// Body : chunked
        /// </summary>
        /// <param name="span"></param>
        /// <param name="bodyStart"></param>
        /// <param name="body"></param>
        /// <param name="pooledBuffer"></param>
        /// <param name="totalConsumed"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestTooLargeException"></exception>
        private bool TryReadChunkedBody(ReadOnlySpan<byte> span, int bodyStart, out ReadOnlySequence<byte> body, out byte[]? pooledBuffer, out int totalConsumed) {
            body = ReadOnlySequence<byte>.Empty;
            pooledBuffer = null;
            totalConsumed = 0;

            int pos = bodyStart;
            int totalLength = span.Length;

            byte[]? rented = null;
            int rentedSize = 0;
            int written = 0;

            while (true) {
                // need more data
                if (pos >= totalLength) {
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    return false;
                }

                ReadOnlySpan<byte> sizeSearch = span.Slice(pos, totalLength - pos);
                int lineEndRel = sizeSearch.IndexOf(Crlf);
                if (lineEndRel < 0) {
                    // need more data
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    return false;
                }

                ReadOnlySpan<byte> sizeLine = sizeSearch.Slice(0, lineEndRel);
                if (!TryParseHexInt(sizeLine, out int chunkSize)) {
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    throw new HttpBadRequestException("Invalid chunk size.");
                }

                pos += lineEndRel + Crlf.Length;

                if (chunkSize == 0) {
                    // last chunk
                    break;
                }

                // check chunk + CRLF
                if (totalLength - pos < chunkSize + Crlf.Length) {
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    return false;
                }

                long newTotal = (long)written + chunkSize;
                if (newTotal > _maxBodySize) {
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    throw new HttpRequestTooLargeException($"Request body too large (chunked): {newTotal} bytes (limit: {_maxBodySize}).");
                }

                // allocation / resize buffer
                if (rented is null) {
                    int initial = Math.Max(chunkSize, 4096);
                    rented = ArrayPool<byte>.Shared.Rent(initial);
                    rentedSize = rented.Length;
                }
                if (written + chunkSize > rentedSize) {
                    int newSize = rentedSize * 2;
                    int required = written + chunkSize;
                    if (newSize < required) {
                        newSize = required;
                    }
                    byte[] newBuf = ArrayPool<byte>.Shared.Rent(newSize);
                    Buffer.BlockCopy(rented, 0, newBuf, 0, written);
                    ArrayPool<byte>.Shared.Return(rented);
                    rented = newBuf;
                    rentedSize = newSize;
                }

                // copy chunk data
                span.Slice(pos, chunkSize).CopyTo(rented.AsSpan(written));
                written += chunkSize;

                pos += chunkSize;

                // CRLF after chunk
                if (totalLength - pos < Crlf.Length) {
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    return false;
                }
                if (!(span[pos] == (byte)'\r' && span[pos + 1] == (byte)'\n')) {
                    if (rented is not null) {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    throw new InvalidOperationException("Invalid chunk terminator.");
                }
                pos += Crlf.Length;
            }

            // pos at trailers start (after "0\r\n")
            ReadOnlySpan<byte> trailerSpan = span.Slice(pos);
            int trailerEndRel = trailerSpan.IndexOf(HeaderTerminator);
            if (trailerEndRel < 0) {
                // need more data
                if (rented is not null) {
                    ArrayPool<byte>.Shared.Return(rented);
                }
                return false;
            }

            int trailerBytesLen = trailerEndRel + HeaderTerminator.Length;
            totalConsumed = pos + trailerBytesLen;

            if (written == 0) {
                body = ReadOnlySequence<byte>.Empty;
                if (rented is not null) {
                    ArrayPool<byte>.Shared.Return(rented);
                }
                pooledBuffer = null;
            }
            else {
                body = new ReadOnlySequence<byte>(rented!, 0, written);
                pooledBuffer = rented;
            }

            return true;
        }

        /// <summary>
        /// Parse Query String
        /// </summary>
        /// <param name="span"></param>
        /// <param name="dict"></param>
        public static void ParseQueryString(ReadOnlySpan<byte> span, Dictionary<string, string> dict) {
            while (!span.IsEmpty) {
                int amp = span.IndexOf((byte)'&');
                ReadOnlySpan<byte> pair = amp >= 0 ? span[..amp] : span;

                if (!pair.IsEmpty) {
                    int eq = pair.IndexOf((byte)'=');
                    if (eq > 0) {
                        ReadOnlySpan<byte> key = pair[..eq];
                        ReadOnlySpan<byte> val = pair[(eq + 1)..];

                        dict[DecodeQueryComponentBytes(key)] = DecodeQueryComponentBytes(val);
                    }
                    else if (eq < 0) {
                        // when there is not value for a key
                        dict[DecodeQueryComponentBytes(pair)] = "";
                    }
                }

                if (amp < 0) {
                    break;
                }
                span = span[(amp + 1)..];
            }
        }

        #region Helpers

        private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> span) {
            int start = 0;
            int end = span.Length - 1;

            while (start <= end && IsAsciiWhitespace(span[start])) {
                start++;
            }
            while (end >= start && IsAsciiWhitespace(span[end])) {
                end--;
            }

            return span.Slice(start, end - start + 1);
        }

        private static bool IsAsciiWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

        private static bool TryParseHexInt(ReadOnlySpan<byte> span, out int value) {
            value = 0;

            if (span.Length == 0) {
                return false;
            }

            // ignore chunk extension after ';'
            int semicolon = span.IndexOf((byte)';');
            if (semicolon >= 0) {
                span = span.Slice(0, semicolon);
            }

            span = TrimAsciiWhitespace(span);
            if (span.Length == 0) {
                return false;
            }

            int result = 0;
            foreach (byte b in span) {
                int digit;
                if (b is >= (byte)'0' and <= (byte)'9') {
                    digit = b - (byte)'0';
                }
                else if (b is >= (byte)'a' and <= (byte)'f') {
                    digit = 10 + (b - (byte)'a');
                }
                else if (b is >= (byte)'A' and <= (byte)'F') {
                    digit = 10 + (b - (byte)'A');
                }
                else {
                    return false;
                }

                // no overflow check, keep it simple
                result = (result << 4) + digit;
            }

            value = result;
            return true;
        }

        /// <summary>
        /// Decode Path Bytes
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
        private static string DecodePathBytes(ReadOnlySpan<byte> span) {
            if (span.Length == 0) {
                return string.Empty;
            }

            // fast path
            if (span.IndexOf((byte)'%') < 0) {
                return Ascii.GetString(span);
            }

            string s = Ascii.GetString(span);
            try {
                return Uri.UnescapeDataString(s);
            }
            catch {
                return s;
            }
        }

        /// <summary>
        /// Decode Query Component Bytes
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
        private static string DecodeQueryComponentBytes(ReadOnlySpan<byte> span) {

            // fast path
            int pct = span.IndexOf((byte)'%');
            int plus = span.IndexOf((byte)'+');
            if (pct < 0 && plus < 0) {
                return Ascii.GetString(span);
            }

            string s = Ascii.GetString(span);
            if (plus >= 0) {
                s = s.Replace('+', ' ');
            }

            try {
                return Uri.UnescapeDataString(s);
            }
            catch {
                return s;
            }
        }

        #endregion
    }

}
