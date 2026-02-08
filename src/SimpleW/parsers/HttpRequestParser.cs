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
        /// ArrayPool
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

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
        /// <param name="bufferPool"></param>
        /// <param name="maxHeaderSize"></param>
        /// <param name="maxBodySize"></param>
        public HttpRequestParser(ArrayPool<byte> bufferPool, int maxHeaderSize, long maxBodySize) {
            _bufferPool = bufferPool;
            _maxHeaderSize = maxHeaderSize;
            _maxBodySize = maxBodySize;
        }

        /// <summary>
        /// Parse HttpRequest from contiguous buffer
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="request"></param>
        /// <param name="consumedBytes"></param>
        /// <exception cref="HttpRequestException"></exception>
        public bool TryReadHttpRequest(in ReadOnlySequence<byte> buffer, HttpRequest request, out long consumedBytes) {
            request.Reset();
            consumedBytes = 0;

            if (buffer.IsEmpty) {
                return false;
            }


            // 1. find header end (CRLF CRLF)
            if (!TryFindHeaderEnd(buffer, out var headerEndPos, out int headerBytesLen)) {
                // check in case we need more data but the header is already too large
                if (buffer.Length > _maxHeaderSize) {
                    throw new HttpRequestException($"Request headers too large: {buffer.Length} bytes (limit: {_maxHeaderSize}).", 413);
                }
                return false; // need more data
            }
            if (headerBytesLen > _maxHeaderSize) {
                throw new HttpRequestException($"Request headers too large: {headerBytesLen} bytes (limit: {_maxHeaderSize}).", 413);
            }
            ReadOnlySequence<byte> headerBlock = buffer.Slice(0, headerEndPos); // header block includes the final CRLFCRLF


            // 2. request line
            SequenceReader<byte> reader = new(headerBlock);
            if (!TryReadLine(ref reader, out ReadOnlySequence<byte> requestLine)) {
                throw new HttpRequestException("Invalid request line.", 400);
            }
            if (!TryParseRequestLine(ToSpanOrPooled(requestLine, out byte[]? pooled1), out string method, out string rawTarget, out string path, out string protocol, out string queryString, out ReadOnlySpan<byte> querySpan)) {
                if (pooled1 != null) {
                    _bufferPool.Return(pooled1);
                }
                throw new HttpRequestException("Invalid request line.", 400);
            }
            if (pooled1 != null) {
                _bufferPool.Return(pooled1);
            }
            // check header
            if (!(protocol.Equals("HTTP/1.0") || protocol.Equals("HTTP/1.1"))) {
                throw new HttpRequestException("HTTP Version Not Supported.", 505, "HTTP Version Not Supported", "Not Supported");
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

            while (TryReadLine(ref reader, out ReadOnlySequence<byte> line)) {
                // empty line => end headers (should not happen because headerBlock stops at CRLFCRLF, but safe)
                if (line.Length == 0) {
                    break;
                }

                ReadOnlySpan<byte> lineSpan = ToSpanOrPooled(line, out var pooled2);

                if (!TryParseHeaderLine(lineSpan, out string? name, out string? value) || name == null) {
                    if (pooled2 != null) {
                        _bufferPool.Return(pooled2);
                    }
                    throw new HttpRequestException("Invalid header line.", 413);
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

                if (pooled2 != null) {
                    _bufferPool.Return(pooled2);
                }
            }
            // check header
            if (request.Protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase) && headers.Host == null) {
                throw new HttpRequestException("Missing Host header (HTTP/1.1).", 400);
            }
            request.ParserSetHeaders(headers);


            // 4. body

            long headerTotalLen = headerBytesLen; // bytes up to and including CRLFCRLF
            ReadOnlySequence<byte> bodyBuffer = buffer.Slice(headerTotalLen);

            // no chunked and no content-length
            if (!isChunked && (!contentLength.HasValue || contentLength.Value == 0)) {
                request.ParserSetBody(ReadOnlySequence<byte>.Empty);
                consumedBytes = headerTotalLen;
                return true;
            }

            if (isChunked) {
                if (!TryReadChunkedBody(bodyBuffer, out ReadOnlySequence<byte> bodySeq, out byte[]? pooledBody, out long bodyConsumed)) {
                    return false;
                }
                request.ParserSetBody(bodySeq);
                request.PooledBodyBuffer = pooledBody;

                consumedBytes = headerTotalLen + bodyConsumed;
                return true;
            }

            // check for body max size (hide warning on contextLengh as we already know it has value)
            long clLong = contentLength!.Value;
            if (clLong > _maxBodySize) {
                throw new HttpRequestException($"Request body too large: {clLong} bytes (limit: {_maxBodySize}).", 413, "Payload Too Large", "HTTP parse");
            }
            if (bodyBuffer.Length < clLong) {
                return false;
            }

            // slice body directly (zero-copy, even across segments)
            request.ParserSetBody(bodyBuffer.Slice(0, clLong));
            consumedBytes = headerTotalLen + clLong;
            return true;
        }

        /// <summary>
        /// We find header end by search CRLF CRLF
        /// headerBytesLen = bytes count up to that point
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="endPos">contains the header CRLK CRLK delimiter</param>
        /// <param name="headerBytesLen"></param>
        /// <returns></returns>
        private static bool TryFindHeaderEnd(in ReadOnlySequence<byte> buffer, out SequencePosition endPos, out int headerBytesLen) {
            SequenceReader<byte> reader = new(buffer);

            // read until CRLF CRLF, keep delimiter
            if (!reader.TryReadTo(out ReadOnlySequence<byte> _, HeaderTerminator, advancePastDelimiter: true)) {
                endPos = default;
                headerBytesLen = 0;
                return false;
            }

            endPos = reader.Position;
            headerBytesLen = (int)reader.Consumed; // safe because headers are bounded by _maxHeaderSize
            return true;
        }

        /// <summary>
        /// Line Reader
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="line">return the line without the CRLF</param>
        /// <returns></returns>
        private static bool TryReadLine(ref SequenceReader<byte> reader, out ReadOnlySequence<byte> line) {
            if (!reader.TryReadTo(out line, Crlf, advancePastDelimiter: true)) {
                return false;
            }
            return true;
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
                throw new HttpRequestException("Missing protocol.", 400);
            }

            method = Ascii.GetString(methodSpan);
            protocol = Ascii.GetString(protocolSpan);

            if (targetSpan.Length == 0) {
                throw new HttpRequestException("Empty request-target.", 400);
            }
            if (targetSpan[0] != (byte)'/') {
                throw new HttpRequestException("Unsupported request-target form.", 400);
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
        /// Body : chunked (works across segments)
        /// </summary>
        /// <param name="bodyBuffer"></param>
        /// <param name="body"></param>
        /// <param name="pooledBuffer"></param>
        /// <param name="totalConsumed"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="HttpRequestException"></exception>
        private bool TryReadChunkedBody(in ReadOnlySequence<byte> bodyBuffer, out ReadOnlySequence<byte> body, out byte[]? pooledBuffer, out long totalConsumed) {
            body = ReadOnlySequence<byte>.Empty;
            pooledBuffer = null;
            totalConsumed = 0;

            SequenceReader<byte> reader = new(bodyBuffer);

            byte[]? rented = null;
            int rentedSize = 0;
            int written = 0;

            try {
                while (true) {
                    // read chunk size line
                    if (!reader.TryReadTo(out ReadOnlySequence<byte> sizeLineSeq, Crlf, advancePastDelimiter: true)) {
                        return false;
                    }

                    ReadOnlySpan<byte> sizeLineSpan = ToSpanOrPooled(sizeLineSeq, out var pooledLine);
                    bool ok = TryParseHexInt(sizeLineSpan, out int chunkSize);
                    if (pooledLine != null) {
                        _bufferPool.Return(pooledLine);
                    }

                    if (!ok) {
                        throw new HttpRequestException("Invalid chunk size.", 400);
                    }

                    if (chunkSize == 0) {
                        break;
                    }

                    long newTotal = (long)written + chunkSize;
                    if (newTotal > _maxBodySize) {
                        throw new HttpRequestException($"Request body too large (chunked): {newTotal} bytes (limit: {_maxBodySize}).", 413, "Payload Too Large", "HTTP parse");
                    }

                    // need chunk bytes + CRLF
                    if (reader.Remaining < chunkSize + Crlf.Length) {
                        return false;
                    }

                    // ensure buffer capacity
                    if (rented == null) {
                        int initial = Math.Max(chunkSize, 4096);
                        rented = _bufferPool.Rent(initial);
                        rentedSize = rented.Length;
                    }
                    if (written + chunkSize > rentedSize) {
                        int newSize = rentedSize * 2;
                        int required = written + chunkSize;
                        if (newSize < required) {
                            newSize = required;
                        }

                        byte[] newBuf = _bufferPool.Rent(newSize);
                        Buffer.BlockCopy(rented, 0, newBuf, 0, written);
                        _bufferPool.Return(rented);
                        rented = newBuf;
                        rentedSize = newSize;
                    }

                    // copy chunk (may be multi-segment)
                    var chunkSeq = reader.Sequence.Slice(reader.Position, chunkSize);
                    CopySequenceTo(chunkSeq, rented.AsSpan(written));
                    written += chunkSize;

                    reader.Advance(chunkSize);

                    // expect CRLF after chunk data
                    if (!reader.TryRead(out byte b1) || !reader.TryRead(out byte b2)) {
                        return false;
                    }

                    if (b1 != (byte)'\r' || b2 != (byte)'\n') {
                        throw new InvalidOperationException("Invalid chunk terminator.");
                    }
                }

                // trailers end with CRLFCRLF
                if (!reader.TryReadTo(out ReadOnlySequence<byte> _, HeaderTerminator, advancePastDelimiter: true)) {
                    return false;
                }

                totalConsumed = reader.Consumed;

                if (written == 0) {
                    if (rented != null) {
                        _bufferPool.Return(rented);
                    }
                    pooledBuffer = null;
                    body = ReadOnlySequence<byte>.Empty;
                }
                else {
                    if (rented == null) {
                        throw new InvalidOperationException("Internal error: chunk buffer is null while written > 0.");
                    }
                    pooledBuffer = rented;
                    body = new ReadOnlySequence<byte>(rented, 0, written);
                }

                return true;
            }
            catch {
                if (rented != null) {
                    _bufferPool.Return(rented);
                }
                throw;
            }
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

        /// <summary>
        /// To Span Or Pooled
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="pooled"></param>
        /// <returns></returns>
        private ReadOnlySpan<byte> ToSpanOrPooled(in ReadOnlySequence<byte> seq, out byte[]? pooled) {
            if (seq.IsSingleSegment) {
                pooled = null;
                return seq.FirstSpan;
            }

            int len = checked((int)seq.Length);
            pooled = _bufferPool.Rent(len);
            seq.CopyTo(pooled);
            return pooled.AsSpan(0, len);
        }

        /// <summary>
        /// CopySequenceTo
        /// </summary>
        /// <param name="seq"></param>
        /// <param name="dest"></param>
        private static void CopySequenceTo(in ReadOnlySequence<byte> seq, Span<byte> dest) {
            int offset = 0;
            foreach (var mem in seq) {
                mem.Span.CopyTo(dest.Slice(offset));
                offset += mem.Length;
            }
        }

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
