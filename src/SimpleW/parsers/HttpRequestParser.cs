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

        private const string HeaderHost = "Host";
        private const string HeaderContentLength = "Content-Length";
        private const string HeaderTransferEncoding = "Transfer-Encoding";
        private const string headerCookie = "Cookie";

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
        /// <param name="foundHeaderEnd"></param>
        /// <exception cref="HttpRequestException"></exception>
        public bool TryReadHttpRequest(in ReadOnlySequence<byte> buffer, HttpRequest request, out long consumedBytes, out bool foundHeaderEnd) {
            request.Reset();
            consumedBytes = 0;
            foundHeaderEnd = false;

            if (buffer.IsEmpty) {
                return false;
            }

            ReadOnlySpan<byte> span = buffer.FirstSpan;
            if (span.Length >= 3 && span[0] == 0x16 && span[1] == 0x03) {
                throw new HttpRequestException($"TLS handshake on HTTP port.", 400);
            }

            // 1. find header end (CRLF CRLF)
            if (!TryFindHeaderEndStrict(buffer, out var headerEndPos, out int headerBytesLen)) {
                // check in case we need more data but the header is already too large
                if (buffer.Length > _maxHeaderSize) {
                    throw new HttpRequestException($"Request headers too large: {buffer.Length} bytes (limit: {_maxHeaderSize}).", 431);
                }
                return false; // need more data
            }
            foundHeaderEnd = true;
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
                if (!protocol.StartsWith("HTTP/1.")) {
                    throw new HttpRequestException("HTTP Version Not Supported.", 400, "HTTP Version Not Supported", "Not Supported");
                }
                protocol = "HTTP/1.1";
            }

            request.ParserSetMethod(method);
            request.ParserSetRawTarget(rawTarget);
            request.ParserSetPath(path);
            request.ParserSetProtocol(protocol);
            request.ParserSetQueryString(queryString);
            ParseQueryString(querySpan, request.Query);


            // 3. headers
            HttpHeaders headers = default;
            long? contentLength = null;
            bool isChunked = false;
            bool hostSeen = false;
            bool contentLengthSeen = false;

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
                    throw new HttpRequestException("Invalid header line.", 400);
                }

                value ??= string.Empty;
                headers.Add(name, value);

                if (name.Equals(HeaderContentLength, StringComparison.OrdinalIgnoreCase)) {
                    if (!TryParseContentLengthStrict(value.AsSpan(), out long parsedCL)) {
                        throw new HttpRequestException("Invalid Content-Length header.", 400);
                    }
                    // if multiple CL headers exist, they must match (otherwise 400).
                    if (contentLengthSeen && contentLength.HasValue && contentLength.Value != parsedCL) {
                        throw new HttpRequestException("Multiple Content-Length headers with different values.", 400);
                    }
                    contentLength = parsedCL;
                    contentLengthSeen = true;
                }
                else if (name.Equals(HeaderTransferEncoding, StringComparison.OrdinalIgnoreCase)) {
                    if (value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) {
                        isChunked = true;
                    }
                }
                else if (name.Equals(HeaderHost, StringComparison.OrdinalIgnoreCase)) {
                    if (hostSeen) {
                        throw new HttpRequestException("Duplicate Host header (HTTP/1.1).", 400);
                    }
                    hostSeen = true;
                }
                else if (name.Equals(headerCookie, StringComparison.OrdinalIgnoreCase)) {
                    if (!IsValidCookieContent(value)) {
                        throw new HttpRequestException("Invalid Cookie (HTTP/1.1).", 400);
                    }
                }

                if (pooled2 != null) {
                    _bufferPool.Return(pooled2);
                }
            }
            // check header
            if (request.Protocol.Equals("HTTP/1.1", StringComparison.OrdinalIgnoreCase)) {
                if (string.IsNullOrWhiteSpace(headers.Host)) {
                    throw new HttpRequestException("Missing Host header (HTTP/1.1).", 400);
                }
                if (headers.Host.IndexOfAny(['@', '/', '\\', '?', '#']) >= 0) {
                    throw new HttpRequestException("Invalid Host header (HTTP/1.1).", 400);
                }
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
        /// Strictly find end of headers: CRLFCRLF.
        /// Enforces RFC line endings while scanning:
        /// - Any LF must be immediately preceded by CR (reject bare LF)
        /// - Any CR must be immediately followed by LF (reject bare CR)
        /// Also enforces _maxHeaderSize early.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="endPos">contains the header CRLK CRLK delimiter</param>
        /// <param name="headerBytesLen"></param>
        private bool TryFindHeaderEndStrict(in ReadOnlySequence<byte> buffer, out SequencePosition endPos, out int headerBytesLen) {
            var reader = new SequenceReader<byte>(buffer);

            int d = 0;              // delimiter state for \r\n\r\n
            bool pendingCr = false; // saw CR, must see LF next

            while (true) {
                if (!reader.TryRead(out byte b)) {
                    endPos = default;
                    headerBytesLen = 0;
                    return false;
                }

                if (reader.Consumed > _maxHeaderSize) {
                    throw new HttpRequestException($"Request headers too large: {reader.Consumed} bytes (limit: {_maxHeaderSize}).", 431);
                }

                // strict line endings
                if (pendingCr) {
                    if (b != (byte)'\n') {
                        throw new HttpRequestException("Invalid line endings in headers (bare CR).", 400);
                    }
                    pendingCr = false;
                }
                else {
                    if (b == (byte)'\n') {
                        throw new HttpRequestException("Invalid line endings in headers (bare LF).", 400);
                    }
                }

                if (b == (byte)'\r') {
                    pendingCr = true;
                }

                // delimiter FSM: \r\n\r\n
                switch (d) {
                    case 0:
                        d = (b == (byte)'\r') ? 1 : 0;
                        break;
                    case 1:
                        d = (b == (byte)'\n') ? 2 : (b == (byte)'\r' ? 1 : 0);
                        break;
                    case 2:
                        d = (b == (byte)'\r') ? 3 : 0;
                        break;
                    case 3:
                        d = (b == (byte)'\n') ? 4 : (b == (byte)'\r' ? 1 : 0);
                        break;
                }

                if (d == 4) {
                    endPos = reader.Position; // after last LF
                    headerBytesLen = (int)reader.Consumed; // <= _maxHeaderSize
                    return true;
                }
            }
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
            CheckRawRequestTarget(targetSpan);
            ReadOnlySpan<byte> protocolSpan = TrimAsciiWhitespace(lineSpan.Slice(secondSpace + 1));

            if (protocolSpan.Length == 0) {
                throw new HttpRequestException("Missing protocol.", 400);
            }

            method = Ascii.GetString(methodSpan);
            protocol = Ascii.GetString(protocolSpan);

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

            // checks
            if (lineSpan.Length == 0) {
                return false;
            }
            int colonIndex = lineSpan.IndexOf(ColonByte);
            if (colonIndex <= 0) {
                return false;
            }

            // name
            ReadOnlySpan<byte> nameSpan = lineSpan.Slice(0, colonIndex);

            // checks
            if (nameSpan.Length == 0) {
                return false;
            }
            if (!IsValidHeaderNameToken(nameSpan)) {
                return false;
            }

            // value
            ReadOnlySpan<byte> valueSpan = TrimAsciiWhitespace(lineSpan.Slice(colonIndex + 1));

            // set
            name = Ascii.GetString(nameSpan);
            value = valueSpan.Length > 0 ? Ascii.GetString(valueSpan) : string.Empty;

            return true;
        }

        /// <summary>
        /// Content-Length Value parser
        /// </summary>
        /// <param name="s"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseContentLengthStrict(ReadOnlySpan<char> s, out long value) {
            value = 0;
            if (s.Length == 0) {
                return false;
            }
            if (s[0] == '+' || s[0] == '-') {
                return false;
            }

            long acc = 0;
            foreach (var ch in s) {
                if (ch < '0' || ch > '9') {
                    return false;
                }
                int digit = ch - '0';
                if (acc > (long.MaxValue - digit) / 10) {
                    return false;
                }
                acc = acc * 10 + digit;
            }
            value = acc;
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
                    // Read chunk-size line
                    if (!reader.TryReadTo(out ReadOnlySequence<byte> sizeLineSeq, Crlf, advancePastDelimiter: true)) {
                        return false;
                    }

                    ReadOnlySpan<byte> sizeLineSpan = ToSpanOrPooled(sizeLineSeq, out var pooledLine);
                    int chunkSize;

                    try {
                        if (!TryParseHexInt(sizeLineSpan, out chunkSize)) {
                            throw new HttpRequestException("Invalid chunk size.", 400);
                        }
                    }
                    finally {
                        if (pooledLine != null) {
                            _bufferPool.Return(pooledLine);
                        }
                    }

                    if (chunkSize < 0) {
                        throw new HttpRequestException("Invalid chunk size.", 400);
                    }

                    if (chunkSize == 0) {
                        // End of chunks.
                        // Then we have either:
                        // - an immediate CRLF => no trailers
                        // - or trailer header lines terminated by an empty line

                        if (reader.Remaining < 2) {
                            return false;
                        }

                        var nextTwo = reader.Sequence.Slice(reader.Position, 2);
                        Span<byte> tmp = stackalloc byte[2];
                        nextTwo.CopyTo(tmp);

                        // Immediate CRLF => no trailers
                        if (tmp[0] == (byte)'\r' && tmp[1] == (byte)'\n') {
                            reader.Advance(2);
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

                        // Otherwise read trailers until empty line
                        while (true) {
                            if (!reader.TryReadTo(out ReadOnlySequence<byte> trailerLineSeq, Crlf, advancePastDelimiter: true)) {
                                return false;
                            }

                            // Empty line => end of trailer section
                            if (trailerLineSeq.Length == 0) {
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

                            // Validate trailer header syntax
                            ReadOnlySpan<byte> trailerSpan = ToSpanOrPooled(trailerLineSeq, out var pooledTrailer);
                            try {
                                if (!TryParseHeaderLine(trailerSpan, out string? trailerName, out string? trailerValue) || trailerName == null) {
                                    throw new HttpRequestException("Invalid trailer header.", 400);
                                }
                            }
                            finally {
                                if (pooledTrailer != null) {
                                    _bufferPool.Return(pooledTrailer);
                                }
                            }
                        }
                    }

                    long newTotal = (long)written + chunkSize;
                    if (newTotal > _maxBodySize) {
                        throw new HttpRequestException($"Request body too large (chunked): {newTotal} bytes (limit: {_maxBodySize}).", 413, "Payload Too Large", "HTTP parse");
                    }

                    // Need chunk bytes + trailing CRLF
                    if (reader.Remaining < chunkSize + Crlf.Length) {
                        return false;
                    }

                    // Ensure capacity
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
                        rentedSize = newBuf.Length;
                    }

                    // Copy chunk data (can span multiple segments)
                    ReadOnlySequence<byte> chunkData = reader.Sequence.Slice(reader.Position, chunkSize);
                    CopySequenceTo(chunkData, rented.AsSpan(written));
                    written += chunkSize;

                    reader.Advance(chunkSize);

                    // Expect CRLF after chunk data
                    if (!reader.TryRead(out byte cr) || !reader.TryRead(out byte lf)) {
                        return false;
                    }

                    if (cr != (byte)'\r' || lf != (byte)'\n') {
                        throw new HttpRequestException("Invalid chunk terminator.", 400);
                    }
                }
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

        /// <summary>
        /// TrimAsciiWhitespace
        /// </summary>
        /// <param name="span"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Lookup Tchar Table
        /// </summary>
        private static readonly byte[] TCharTable = BuildTCharTable();

        /// <summary>
        /// Build TChar Table
        /// </summary>
        /// <returns></returns>
        private static byte[] BuildTCharTable() {
            byte[] t = new byte[128];

            for (int c = '0'; c <= '9'; c++) {
                t[c] = 1;
            }
            for (int c = 'A'; c <= 'Z'; c++) {
                t[c] = 1;
            }
            for (int c = 'a'; c <= 'z'; c++) {
                t[c] = 1;
            }

            const string extra = "!#$%&'*+-.^_`|~";
            foreach (char ch in extra) {
                t[ch] = 1;
            }

            return t;
        }

        /// <summary>
        /// Check if a header has a valid name
        /// </summary>
        /// <param name="nameSpan"></param>
        /// <returns></returns>
        private static bool IsValidHeaderNameToken(ReadOnlySpan<byte> nameSpan) {
            if (nameSpan.Length == 0) {
                return false;
            }

            foreach (byte b in nameSpan) {
                if (b >= 128) {
                    return false; // non-ASCII => nope
                }
                if (TCharTable[b] == 0) {
                    return false; // also rejects space/tab/ctl
                }
            }
            return true;
        }

        /// <summary>
        /// Check if cookie contains invalid char
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool IsValidCookieContent(string value) {
            foreach (char c in value) {
                if (c <= 0x1F || c == 0x7F) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Check Request RawTarget
        /// </summary>
        /// <param name="targetSpan"></param>
        /// <returns></returns>
        /// <exception cref="HttpRequestException"></exception>
        private static void CheckRawRequestTarget(ReadOnlySpan<byte> targetSpan) {
            if (targetSpan.Length == 0) {
                throw new HttpRequestException("Empty request-target.", 400);
            }
            if (targetSpan[0] != (byte)'/') {
                throw new HttpRequestException("Unsupported request-target form.", 400);
            }
            foreach (byte b in targetSpan) {
                // reject controls
                if (b <= 0x1F || b == 0x7F) {
                    throw new HttpRequestException("Invalid request-target.", 400);
                }
                // reject non-ASCII raw bytes
                if (b >= 0x80) {
                    throw new HttpRequestException("Invalid request-target.", 400);
                }
            }
        }

        /// <summary>
        /// IsAsciiWhitespace
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        private static bool IsAsciiWhitespace(byte b) => b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

        /// <summary>
        /// TryParseHexInt
        /// </summary>
        /// <param name="span"></param>
        /// <param name="value"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Convert byte to string
        /// </summary>
        /// <param name="seq"></param>
        /// <returns></returns>
        private static string DumpBytes(ReadOnlySequence<byte> seq) {
            byte[] arr = seq.ToArray();
            StringBuilder sb = new();

            for (int i = 0; i < arr.Length; i++) {
                if (i > 0 && i % 16 == 0) {
                    sb.AppendLine();
                }
                sb.Append(arr[i].ToString("X2")).Append(' ');
            }

            sb.AppendLine();
            sb.AppendLine("ASCII:");
            foreach (var b in arr) {
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            return sb.ToString();
        }

        #endregion
    }

}
