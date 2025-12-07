using System.Buffers;
using System.Text;


namespace SimpleW {

    /// <summary>
    /// HTTP request is used to create or process parameters of HTTP protocol request (method, URL, headers, etc).
    /// </summary>
    public sealed class HttpRequest {

        /// <summary>
        /// Get the HTTP request method
        /// </summary>
        public string Method { get; internal set; } = string.Empty;

        /// <summary>
        /// Get the HTTP request Path
        /// </summary>
        public string Path { get; internal set; } = string.Empty;

        /// <summary>
        /// Raw target (path + query)
        /// </summary>
        public string RawTarget { get; internal set; } = string.Empty;

        /// <summary>
        /// HTTP protocol version (e.g. "HTTP/1.1")
        /// </summary>
        public string Protocol { get; internal set; } = string.Empty;

        /// <summary>
        /// HTTP headers (case-insensitive)
        /// </summary>
        public HttpHeaders Headers;

        /// <summary>
        /// Body (buffer only valid during the time of the underlying Handler)
        /// </summary>
        public ReadOnlySequence<byte> Body { get; internal set; } = ReadOnlySequence<byte>.Empty;

        /// <summary>
        /// Body as String (only valid during the time of the underlying Handler)
        /// </summary>
        public string BodyString {
            get {
                if (Body.IsEmpty) {
                    return string.Empty;
                }

                if (Body.IsSingleSegment) {
                    return Utf8.GetString(Body.FirstSpan);
                }

                // multi segments -> copy to temp buffer via ArrayPool
                int length = checked((int)Body.Length);
                byte[] rented = ArrayPool<byte>.Shared.Rent(length);

                try {
                    Body.CopyTo(rented);
                    return Utf8.GetString(rented, 0, length);
                }
                finally {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        /// <summary>
        /// QueryString
        /// </summary>
        public string QueryString { get; internal set;  } = string.Empty;

        /// <summary>
        /// Flag to Query parsing
        /// </summary>
        private bool _queryInitialized = false;

        /// <summary>
        /// QueryString Dictionnary parsing
        /// </summary>
        private Dictionary<string, string>? _query;

        /// <summary>
        /// QueryString Dictionnary
        /// </summary>
        public Dictionary<string, string> Query {
            get {
                if (!_queryInitialized) {
                    _queryInitialized = true;
                    _query ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(QueryString)) {
                        HttpRequestParserState.ParseQueryString(QueryString.AsSpan(), _query);
                    }
                }
                return _query!;
            }
        }

        /// <summary>
        /// Reset HttpRequest for reuse
        /// </summary>
        internal void Reset() {
            Method = string.Empty;
            Path = string.Empty;
            RawTarget = string.Empty;
            Protocol = string.Empty;
            QueryString = string.Empty;

            Headers = default;
            Body = ReadOnlySequence<byte>.Empty;

            _queryInitialized = false;
            _query?.Clear();
        }

        #region buffer

        /// <summary>
        /// public Property to received a buffer from an ArrayPool
        /// </summary>
        internal byte[]? PooledBodyBuffer { get; set; }

        /// <summary>
        /// Return the buffer to ArrayPool
        /// </summary>
        internal void ReturnPooledBodyBuffer() {
            if (PooledBodyBuffer is not null) {
                ArrayPool<byte>.Shared.Return(PooledBodyBuffer);
                PooledBodyBuffer = null;
            }
        }

        #endregion buffer

        #region helpers

        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        #endregion helpers

        // plus tard :
        // public Dictionary<string, string?> Query { get; } = new(StringComparer.OrdinalIgnoreCase);
        // public Dictionary<string, string> RouteValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// HttpHeaders
    ///     1. most common
    ///     2. fallback list
    /// </summary>
    public struct HttpHeaders {

        // most common headers
        public string? Host;
        public string? ContentType;
        public string? ContentLengthRaw;   // raw string "123", parser use long
        public string? UserAgent;
        public string? Accept;
        public string? AcceptEncoding;
        public string? AcceptLanguage;
        public string? Connection;
        public string? TransferEncoding;
        public string? Cookie;

        // fallback for all other headers
        private HeaderEntry[]? _other;
        private int _otherCount;

        /// <summary>
        /// Add a Header (name/value).
        /// Set most common else save in _other
        /// </summary>
        public void Add(string name, string value) {
            // most common headers
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) {
                Host = value;
                return;
            }
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) {
                ContentType = value;
                return;
            }
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                ContentLengthRaw = value;
                return;
            }
            if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) {
                UserAgent = value;
                return;
            }
            if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase)) {
                Accept = value;
                return;
            }
            if (name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) {
                AcceptEncoding = value;
                return;
            }
            if (name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase)) {
                AcceptLanguage = value;
                return;
            }
            if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) {
                Connection = value;
                return;
            }
            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) {
                TransferEncoding = value;
                return;
            }
            if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) {
                Cookie = value;
                return;
            }

            // fallback
            AddFallback(name, value);
        }

        /// <summary>
        /// TryGetValue (headers connus + autres).
        /// </summary>
        public bool TryGetValue(string name, out string? value) {
            // most common headers
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) {
                value = Host;
                return value is not null;
            }
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) {
                value = ContentType;
                return value is not null;
            }
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                value = ContentLengthRaw;
                return value is not null;
            }
            if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) {
                value = UserAgent;
                return value is not null;
            }
            if (name.Equals("Accept", StringComparison.OrdinalIgnoreCase)) {
                value = Accept;
                return value is not null;
            }
            if (name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase)) {
                value = AcceptEncoding;
                return value is not null;
            }
            if (name.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase)) {
                value = AcceptLanguage;
                return value is not null;
            }
            if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) {
                value = Connection;
                return value is not null;
            }
            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) {
                value = TransferEncoding;
                return value is not null;
            }
            if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase)) {
                value = Cookie;
                return value is not null;
            }

            // fallback
            if (_other is not null) {
                for (int i = 0; i < _otherCount; i++) {
                    ref readonly HeaderEntry entry = ref _other[i];
                    if (entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                        value = entry.Value;
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Enumère tous les headers (connus + autres).
        /// Utile si tu veux loguer, etc.
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> EnumerateAll() {
            // most common headers
            if (Host is not null) {
                yield return new("Host", Host);
            }
            if (ContentType is not null) {
                yield return new("Content-Type", ContentType);
            }
            if (ContentLengthRaw is not null) {
                yield return new("Content-Length", ContentLengthRaw);
            }
            if (UserAgent is not null) {
                yield return new("User-Agent", UserAgent);
            }
            if (Accept is not null) {
                yield return new("Accept", Accept);
            }
            if (AcceptEncoding is not null) {
                yield return new("Accept-Encoding", AcceptEncoding);
            }
            if (AcceptLanguage is not null) {
                yield return new("Accept-Language", AcceptLanguage);
            }
            if (Connection is not null) {
                yield return new("Connection", Connection);
            }
            if (TransferEncoding is not null) {
                yield return new("Transfer-Encoding", TransferEncoding);
            }
            if (Cookie is not null) {
                yield return new("Cookie", Cookie);
            }

            // fallback
            if (_other is not null) {
                for (int i = 0; i < _otherCount; i++) {
                    ref readonly HeaderEntry e = ref _other[i];
                    yield return new(e.Name, e.Value);
                }
            }
        }

        /// <summary>
        /// Try get a cookie by name from the Cookie header.
        /// Cookie names are compared case-sensitively (RFC).
        /// </summary>
        public bool TryGetCookie(string name, out string? value) {
            value = null;

            if (Cookie is null || string.IsNullOrEmpty(name)) {
                return false;
            }

            ReadOnlySpan<char> span = Cookie.AsSpan();
            int start = 0;

            while (start < span.Length) {
                // find end of current "name=value" pair (separated by ';')
                int separator = span.Slice(start).IndexOf(';');
                ReadOnlySpan<char> segment = separator >= 0 ? span.Slice(start, separator) : span.Slice(start);

                segment = segment.Trim();
                if (!segment.IsEmpty) {
                    int eqIndex = segment.IndexOf('=');
                    if (eqIndex > 0) {
                        ReadOnlySpan<char> nameSpan = segment.Slice(0, eqIndex).Trim();
                        ReadOnlySpan<char> valueSpan = segment.Slice(eqIndex + 1).Trim();

                        if (nameSpan.Equals(name.AsSpan(), StringComparison.Ordinal)) {
                            value = valueSpan.ToString();
                            return true;
                        }
                    }
                }

                if (separator < 0) {
                    break;
                }

                // skip ';'
                start += separator + 1;
            }

            return false;
        }

        /// <summary>
        /// Enumerate all cookies as key/value pairs.
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> EnumerateCookies() {
            if (Cookie is null) {
                yield break;
            }

            string cookie = Cookie;
            int length = cookie.Length;
            int start = 0;

            while (start < length) {
                int separator = cookie.IndexOf(';', start);
                int end = separator >= 0 ? separator : length;

                // segment = "name=value"
                string segment = cookie.Substring(start, end - start).Trim();
                if (segment.Length > 0) {
                    int eqIndex = segment.IndexOf('=');
                    if (eqIndex > 0) {
                        string name = segment.Substring(0, eqIndex).Trim();
                        string value = segment.Substring(eqIndex + 1).Trim();

                        if (name.Length > 0) {
                            yield return new KeyValuePair<string, string>(name, value);
                        }
                    }
                }

                if (separator < 0) {
                    break;
                }

                start = separator + 1;
            }
        }

        /// <summary>
        /// Add header to array
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        private void AddFallback(string name, string value) {
            if (_other is null) {
                _other = new HeaderEntry[4];
            }
            else if (_otherCount == _other.Length) {
                Array.Resize(ref _other, _other.Length * 2);
            }
            _other[_otherCount++] = new HeaderEntry(name, value);
        }
    }

    /// <summary>
    /// HeaderEntry
    /// </summary>
    public readonly struct HeaderEntry {
        public readonly string Name;
        public readonly string Value;

        public HeaderEntry(string name, string value) {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// HttpRequest Parser Instance
    /// </summary>
    internal struct HttpRequestParserState {

        #region Constants & shared fields

        private const int MaxStackLineSize = 256;

        private static readonly byte[] Crlf = { (byte)'\r', (byte)'\n' };
        private static readonly byte[] HeaderTerminator = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

        private const byte SpaceByte = (byte)' ';
        private const byte ColonByte = (byte)':';

        private const string HeaderContentLength = "Content-Length";
        private const string HeaderTransferEncoding = "Transfer-Encoding";

        private static readonly Encoding Ascii = Encoding.ASCII;

        #endregion

        private readonly int _maxHeaderSize;
        private readonly long _maxBodySize;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="maxHeaderSize"></param>
        /// <param name="maxBodySize"></param>
        public HttpRequestParserState(int maxHeaderSize, long maxBodySize) {
            _maxHeaderSize = maxHeaderSize;
            _maxBodySize = maxBodySize;
        }

        /// <summary>
        /// Parse HttpRequest from Buffer
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="request"></param>
        /// <param name="maxHeaderSize"></param>
        /// <param name="maxBodySize"></param>
        public bool TryReadHttpRequest(ref ReadOnlySequence<byte> buffer, HttpRequest request) {
            request.Reset();

            SequenceReader<byte> reader = new(buffer);

            // 1. read headers until CRLF CRLF
            if (!reader.TryReadTo(out ReadOnlySequence<byte> headerSequence, HeaderTerminator, advancePastDelimiter: true)) {
                return false; // no enough data for headers
            }

            // check for headers max size
            if (headerSequence.Length > _maxHeaderSize) {
                throw new HttpRequestTooLargeException($"Request headers too large: {headerSequence.Length} bytes (limit: {_maxHeaderSize}).");
            }

            // mark the body start in the reader
            SequencePosition bodyStart = reader.Position;

            // 2. parser request line + headers
            if (!TryParseStartLineAndHeaders(headerSequence, request, out long? contentLength, out bool isChunked)) {
                return false; // invalid request
            }

            // if no content-length and no chunked
            if (!isChunked && (!contentLength.HasValue || contentLength.Value == 0)) {
                request.Body = ReadOnlySequence<byte>.Empty;
                buffer = buffer.Slice(bodyStart);
                return true;
            }

            ReadOnlySequence<byte> fullSequence = buffer;

            // 3. read the body if it exists
            if (isChunked) {
                if (!TryReadChunkedBody(fullSequence, bodyStart, out ReadOnlySequence<byte> body, out byte[]? pooledBuffer, out SequencePosition consumedTo)) {
                    return false; // need more data
                }

                request.Body = body;
                request.PooledBodyBuffer = pooledBuffer; // associate ArrayPool buffer to current req
                buffer = fullSequence.Slice(consumedTo);
            }
            else if (contentLength.HasValue && contentLength.Value > 0) {

                // check for body size
                if (contentLength.Value > _maxBodySize) {
                    throw new HttpRequestTooLargeException($"Request body too large: {contentLength.Value} bytes (limit: {_maxBodySize}).");
                }

                if (!TryReadContentLengthBody(fullSequence, bodyStart, contentLength.Value, out ReadOnlySequence<byte> body, out SequencePosition consumedTo)) {
                    return false; // need more data
                }

                request.Body = body;
                buffer = fullSequence.Slice(consumedTo);
            }

            return true;
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
        public int TryReadHttpRequestFast(byte[] buffer, int offset, int length, HttpRequest request) {
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

            ReadOnlySpan<byte> headerSpan = span.Slice(0, headerEnd);

            // 2. request line
            int firstCrlf = headerSpan.IndexOf(Crlf);
            if (firstCrlf <= 0) {
                return 0; // invalid
            }

            ReadOnlySpan<byte> requestLineSpan = headerSpan.Slice(0, firstCrlf);
            if (!TryParseRequestLineFast(requestLineSpan, out string method, out string rawTarget, out string path, out string protocol, out string queryString)) {
                return 0;
            }

            request.Method = method;
            request.RawTarget = rawTarget;
            request.Path = path;
            request.Protocol = protocol;
            request.QueryString = queryString;

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

                if (!TryParseHeaderLineFast(lineSpan, out string? name, out string? value) || name is null) {
                    continue;
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

            request.Headers = headers;

            // no content-length and no chunked
            if (!isChunked && (!contentLength.HasValue || contentLength.Value == 0)) {
                request.Body = ReadOnlySequence<byte>.Empty;
                return headerBytesLen;
            }

            // 4. body (Content-Length ou chunked)
            int bodyStart = headerBytesLen;
            int availableBodyBytes = length - bodyStart;

            // chunked
            if (isChunked) {
                if (!TryReadChunkedBodyFast(span, bodyStart, out ReadOnlySequence<byte> bodySeq, out byte[]? pooledBuffer, out int totalConsumed)) {
                    // need more data
                    return 0;
                }

                request.Body = bodySeq;
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
            request.Body = new ReadOnlySequence<byte>(buffer, bodyOffsetInBuffer, bodyLength);

            // total consumed = headers + body
            return headerBytesLen + bodyLength;
        }

        /// <summary>
        /// Line and Header
        /// </summary>
        /// <param name="headerSequence"></param>
        /// <param name="request"></param>
        /// <param name="contentLength"></param>
        /// <param name="isChunked"></param>
        /// <returns></returns>
        private static bool TryParseStartLineAndHeaders(in ReadOnlySequence<byte> headerSequence, HttpRequest request, out long? contentLength, out bool isChunked) {
            contentLength = null;
            isChunked = false;

            SequenceReader<byte> headerReader = new(headerSequence);

            // request line
            if (!headerReader.TryReadTo(out ReadOnlySequence<byte> requestLineSeq, Crlf, advancePastDelimiter: true)) {
                return false;
            }

            if (!TryParseRequestLine(requestLineSeq, out string method, out string rawTarget, out string path, out string protocol, out string queryString)) {
                return false;
            }

            request.Method = method.ToUpperInvariant();
            request.RawTarget = rawTarget;
            request.Path = path;
            request.Protocol = protocol;
            request.QueryString = queryString;

            // headers
            HttpHeaders headers = default;
            while (headerReader.Remaining > 0) {

                if (!headerReader.TryReadTo(out ReadOnlySequence<byte> headerLineSeq, Crlf, advancePastDelimiter: true)) {
                    break;
                }
                if (headerLineSeq.Length == 0) {
                    continue;
                }
                if (!TryParseHeaderLine(headerLineSeq, out string? name, out string? value) || name is null) {
                    continue;
                }

                value ??= string.Empty;

                headers.Add(name, value);

                // important header Content-Length
                if (name.Equals(HeaderContentLength, StringComparison.OrdinalIgnoreCase)) {
                    if (long.TryParse(value, out long cl) && cl >= 0) {
                        contentLength = cl;
                    }
                }
                // important header Transfer-Encoding
                else if (name.Equals(HeaderTransferEncoding, StringComparison.OrdinalIgnoreCase)) {
                    if (value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) is int idx && idx >= 0) {
                        isChunked = true;
                    }
                }
            }

            request.Headers = headers;
            return true;
        }

        /// <summary>
        /// Request Line
        /// </summary>
        /// <param name="lineSeq"></param>
        /// <param name="method"></param>
        /// <param name="rawTarget"></param>
        /// <param name="path"></param>
        /// <param name="protocol"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        private static bool TryParseRequestLine(in ReadOnlySequence<byte> lineSeq, out string method, out string rawTarget, out string path, out string protocol, out string queryString) {
            method = rawTarget = path = protocol = string.Empty;
            queryString = string.Empty;

            int len = (int)lineSeq.Length;
            if (len == 0) {
                return false;
            }

            Span<byte> lineSpan = len <= MaxStackLineSize ? stackalloc byte[len] : default;

            byte[]? rented = null;
            if (len > MaxStackLineSize) {
                rented = ArrayPool<byte>.Shared.Rent(len);
                lineSpan = rented.AsSpan(0, len);
            }

            try {
                lineSeq.CopyTo(lineSpan);

                // METHOD SP TARGET SP HTTP/x.x
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

                // rest of line = protocole (HTTP/x.x)
                ReadOnlySpan<byte> protocolSpan = lineSpan.Slice(secondSpace + 1);
                protocolSpan = TrimAsciiWhitespace(protocolSpan);
                if (protocolSpan.Length == 0) {
                    return false;
                }

                method = Ascii.GetString(methodSpan);
                rawTarget = Ascii.GetString(targetSpan);
                protocol = Ascii.GetString(protocolSpan);

                // path & query
                int qIndex = rawTarget.IndexOf('?', StringComparison.Ordinal);
                if (qIndex >= 0) {
                    path = rawTarget.Substring(0, qIndex);
                    queryString = rawTarget.Substring(qIndex + 1);
                }
                else {
                    path = rawTarget;
                }

                return true;
            }
            finally {
                if (rented is not null) {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
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
        /// <returns></returns>
        private static bool TryParseRequestLineFast(ReadOnlySpan<byte> lineSpan, out string method, out string rawTarget, out string path, out string protocol, out string queryString) {
            method = rawTarget = path = protocol = string.Empty;
            queryString = string.Empty;

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
                return false;
            }

            method = Ascii.GetString(methodSpan);
            rawTarget = Ascii.GetString(targetSpan);
            protocol = Ascii.GetString(protocolSpan);

            int qIndex = rawTarget.IndexOf('?', StringComparison.Ordinal);
            if (qIndex >= 0) {
                path = rawTarget.Substring(0, qIndex);
                queryString = rawTarget[(qIndex + 1)..];
            }
            else {
                path = rawTarget;
            }

            return true;
        }

        /// <summary>
        /// Header
        /// </summary>
        /// <param name="lineSeq"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseHeaderLine(in ReadOnlySequence<byte> lineSeq, out string? name, out string? value) {
            name = null;
            value = null;

            int len = (int)lineSeq.Length;
            if (len == 0) {
                return false;
            }

            Span<byte> lineSpan = len <= MaxStackLineSize ? stackalloc byte[len] : default;

            byte[]? rented = null;
            if (len > MaxStackLineSize) {
                rented = ArrayPool<byte>.Shared.Rent(len);
                lineSpan = rented.AsSpan(0, len);
            }

            try {
                lineSeq.CopyTo(lineSpan);

                int colonIndex = lineSpan.IndexOf(ColonByte);
                if (colonIndex <= 0) {
                    return false;
                }

                ReadOnlySpan<byte> nameSpan = lineSpan.Slice(0, colonIndex);
                ReadOnlySpan<byte> valueSpan = lineSpan.Slice(colonIndex + 1);

                nameSpan = TrimAsciiWhitespace(nameSpan);
                valueSpan = TrimAsciiWhitespace(valueSpan);

                if (nameSpan.Length == 0) {
                    return false;
                }

                name = Ascii.GetString(nameSpan);
                value = valueSpan.Length > 0 ? Ascii.GetString(valueSpan) : string.Empty;

                return true;
            }
            finally {
                if (rented is not null) {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        /// <summary>
        /// Header
        /// </summary>
        /// <param name="lineSpan"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryParseHeaderLineFast(ReadOnlySpan<byte> lineSpan, out string? name, out string? value) {
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
        /// Body: Content-Length
        /// </summary>
        /// <param name="fullSequence"></param>
        /// <param name="bodyStart"></param>
        /// <param name="contentLength"></param>
        /// <param name="body"></param>
        /// <param name="consumedTo"></param>
        /// <returns></returns>
        private static bool TryReadContentLengthBody(in ReadOnlySequence<byte> fullSequence, SequencePosition bodyStart, long contentLength, out ReadOnlySequence<byte> body, out SequencePosition consumedTo) {
            body = ReadOnlySequence<byte>.Empty;
            consumedTo = bodyStart;

            // no body
            if (contentLength == 0) {
                return true;
            }

            // sequence to the body's start
            ReadOnlySequence<byte> bodySeq = fullSequence.Slice(bodyStart);

            if (bodySeq.Length < contentLength) {
                // not enough data yet
                return false;
            }

            // update body (can't be multi-segment)
            body = bodySeq.Slice(0, contentLength);

            // update the consume pointer
            consumedTo = body.End;
            return true;
        }

        /// <summary>
        /// Body : chunked
        /// </summary>
        /// <param name="fullSequence"></param>
        /// <param name="bodyStart"></param>
        /// <param name="body"></param>
        /// <param name="pooledBuffer"></param>
        /// <param name="consumedTo"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private bool TryReadChunkedBody(in ReadOnlySequence<byte> fullSequence, SequencePosition bodyStart, out ReadOnlySequence<byte> body, out byte[]? pooledBuffer, out SequencePosition consumedTo) {
            body = ReadOnlySequence<byte>.Empty;
            pooledBuffer = null;
            consumedTo = bodyStart;

            var reader = new SequenceReader<byte>(fullSequence.Slice(bodyStart));

            using var chunks = new MemoryStream();

            while (true) {
                // 1. read the "chunk-size" line
                if (!reader.TryReadTo(out ReadOnlySequence<byte> sizeLineSeq, Crlf, advancePastDelimiter: true)) {
                    return false; // need more data
                }

                if (!TryParseHexInt(sizeLineSeq, out int chunkSize)) {
                    throw new InvalidOperationException("Invalid chunk size.");
                }

                if (chunkSize == 0) {
                    // final chunk
                    break;
                }

                // 2. check for chunkSize bytes (+ CRLF after)
                if (reader.Remaining < chunkSize + 2) {
                    return false;
                }

                // 3. check for body max size
                long newTotal = chunks.Length + chunkSize;
                if (newTotal > _maxBodySize) {
                    throw new HttpRequestTooLargeException($"Request body too large (chunked): {newTotal} bytes (limit: {_maxBodySize}).");
                }

                // 4. read chunkSize bytes
                ReadOnlySequence<byte> chunkData = reader.Sequence.Slice(reader.Position, chunkSize);

                foreach (var seg in chunkData) {
                    chunks.Write(seg.Span);
                }

                reader.Advance(chunkSize);

                // 5. consume the CRLF following the chunk
                if (!reader.TryReadTo(out ReadOnlySequence<byte> _, Crlf, advancePastDelimiter: true)) {
                    return false;
                }
            }

            // 6. optionnal trailers + final CRLFCRLF
            SequencePosition afterZeroChunkPos = reader.Position;
            ReadOnlySequence<byte> remaining = reader.Sequence.Slice(afterZeroChunkPos);

            SequenceReader<byte> trailerReader = new(remaining);
            if (trailerReader.TryReadTo(out ReadOnlySequence<byte> _, HeaderTerminator, advancePastDelimiter: true)) {
                consumedTo = trailerReader.Position;
            }
            else {
                // no trailers, stop
                consumedTo = reader.Position;
            }

            // buffer alloc via ArrayPool
            int len = (int)chunks.Length;
            byte[] rented = ArrayPool<byte>.Shared.Rent(len);

            chunks.Position = 0;
            int read = chunks.Read(rented, 0, len);

            body = new ReadOnlySequence<byte>(rented, 0, read);
            pooledBuffer = rented; // ouput the buffer

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
        private bool TryReadChunkedBodyFast(ReadOnlySpan<byte> span, int bodyStart, out ReadOnlySequence<byte> body, out byte[]? pooledBuffer, out int totalConsumed) {
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
                    throw new InvalidOperationException("Invalid chunk size.");
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
        public static void ParseQueryString(ReadOnlySpan<char> span, Dictionary<string, string> dict) {
            while (!span.IsEmpty) {
                int amp = span.IndexOf('&');
                ReadOnlySpan<char> pair = amp >= 0 ? span[..amp] : span;

                if (!pair.IsEmpty) {
                    int eq = pair.IndexOf('=');
                    if (eq > 0) {
                        ReadOnlySpan<char> key = pair[..eq];
                        ReadOnlySpan<char> val = pair[(eq + 1)..];

                        // TODO : the URL need to be decoded first
                        dict[key.ToString()] = val.ToString();
                    }
                    else {
                        // when there is not value for a key
                        dict[pair.ToString()] = "";
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

        private static bool TryParseHexInt(in ReadOnlySequence<byte> seq, out int value) {
            value = 0;

            int len = (int)seq.Length;
            if (len == 0) {
                return false;
            }

            Span<byte> span = len <= MaxStackLineSize ? stackalloc byte[len] : default;

            byte[]? rented = null;
            if (len > MaxStackLineSize) {
                rented = ArrayPool<byte>.Shared.Rent(len);
                span = rented.AsSpan(0, len);
            }

            try {
                seq.CopyTo(span);

                ReadOnlySpan<byte> workSpan = span;

                // ignore chunk extension after ';'
                int semicolon = workSpan.IndexOf((byte)';');
                if (semicolon >= 0) {
                    workSpan = workSpan.Slice(0, semicolon);
                }

                workSpan = TrimAsciiWhitespace(workSpan);
                if (workSpan.Length == 0) {
                    return false;
                }

                int result = 0;
                foreach (byte b in workSpan) {
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
            finally {
                if (rented is not null) {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

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

        #endregion
    }

}
