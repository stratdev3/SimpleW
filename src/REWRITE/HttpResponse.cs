using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;


namespace SimpleW {

    /// <summary>
    /// HttpResponse builder
    /// Owns status line, headers, body, and can send via HttpSession.SendAsync()
    /// </summary>
    public sealed class HttpResponse {

        /// <summary>
        /// The Session
        /// </summary>
        private readonly HttpSession _session;

        /// <summary>
        /// The ArrayPool
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// Flag Response Sent
        /// </summary>
        private bool _sent;

        /// <summary>
        /// Status Code
        /// </summary>
        private int _statusCode;

        /// <summary>
        /// Status Text
        /// </summary>
        private string _statusText;

        /// <summary>
        /// Content Type
        /// </summary>
        private string? _contentType;

        /// <summary>
        /// Headers
        /// </summary>
        private HeaderEntry[] _headers;

        /// <summary>
        /// Header count
        /// </summary>
        private int _headerCount;

        /// <summary>
        /// True if user added a Content-Length via AddHeader (so we must not auto-write it)
        /// </summary>
        private bool _hasCustomContentLength;

        //
        // BODY
        //

        /// <summary>
        /// Kind of Body
        /// </summary>
        private BodyKind _bodyKind;

        /// <summary>
        /// for BodyKind.Memory
        /// </summary>
        private ReadOnlyMemory<byte> _bodyMemory;

        /// <summary>
        /// for BodyKind.Segment / OwnedBuffer
        /// </summary>
        private byte[]? _bodyArray;
        private int _bodyOffset;
        private int _bodyLength;

        /// <summary>
        /// owned body writer (ArrayPool)
        /// </summary>
        private PooledBufferWriter? _ownedBodyWriter;

        /// <summary>
        /// optional owner for lifetime-managed body
        /// </summary>
        private IDisposable? _bodyOwner;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="session"></param>
        /// <param name="bufferPool"></param>
        public HttpResponse(HttpSession session, ArrayPool<byte> bufferPool) {
            _session = session;
            _bufferPool = bufferPool;

            _statusCode = 200;
            _statusText = "OK";

            _headers = new HeaderEntry[8];
            _headerCount = 0;

            _bodyKind = BodyKind.None;
            _bodyMemory = ReadOnlyMemory<byte>.Empty;
            _bodyArray = null;
            _bodyOffset = 0;
            _bodyLength = 0;

            _cookieCount = 0;

            _sent = false;
        }

        /// <summary>
        /// Status
        /// </summary>
        /// <param name="statusCode"></param>
        /// <param name="statusText"></param>
        /// <returns></returns>
        public HttpResponse Status(int statusCode, string? statusText = null) {
            _statusCode = statusCode;
            _statusText = statusText ?? DefaultStatusText(statusCode);
            return this;
        }

        /// <summary>
        /// Get the Default Status Text for a status code
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static string DefaultStatusText(int code) => code switch {

            100 => "Continue",
            101 => "Switching Protocols",
            102 => "Processing",
            103 => "Early Hints",

            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            203 => "Non-Authoritative Information",
            204 => "No Content",
            205 => "Reset Content",
            206 => "Partial Content",
            207 => "Multi-Status",
            208 => "Already Reported",

            226 => "IM Used",

            300 => "Multiple Choices",
            301 => "Moved Permanently",
            302 => "Found",
            303 => "See Other",
            304 => "Not Modified",
            305 => "Use Proxy",
            306 => "Switch Proxy",
            307 => "Temporary Redirect",
            308 => "Permanent Redirect",

            400 => "Bad Request",
            401 => "Unauthorized",
            402 => "Payment Required",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            406 => "Not Acceptable",
            407 => "Proxy Authentication Required",
            408 => "Request Timeout",
            409 => "Conflict",
            410 => "Gone",
            411 => "Length Required",
            412 => "Precondition Failed",
            413 => "Payload Too Large",
            414 => "URI Too Long",
            415 => "Unsupported Media Type",
            416 => "Range Not Satisfiable",
            417 => "Expectation Failed",

            421 => "Misdirected Request",
            422 => "Unprocessable Entity",
            423 => "Locked",
            424 => "Failed Dependency",
            425 => "Too Early",
            426 => "Upgrade Required",
            427 => "Unassigned",
            428 => "Precondition Required",
            429 => "Too Many Requests",
            431 => "Request Header Fields Too Large",

            451 => "Unavailable For Legal Reasons",

            500 => "Internal Server Error",
            501 => "Not Implemented",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            505 => "HTTP Version Not Supported",
            506 => "Variant Also Negotiates",
            507 => "Insufficient Storage",
            508 => "Loop Detected",

            510 => "Not Extended",
            511 => "Network Authentication Required",

            _ => "Unknown"
        };

        /// <summary>
        /// Set ContentType
        /// </summary>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse ContentType(string contentType) {
            _contentType = contentType;
            return this;
        }

        /// <summary>
        /// Set ContentType from a file extension (e.g: ".html")
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public HttpResponse ContextTypeFromExtension(string extension) {
            _contentType = DefaultContentType(extension);
            return this;
        }

        /// <summary>
        /// Get Content Type from a file extension (e.g: ".html")
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public static string DefaultContentType(string extension) => extension switch {

            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".vue" => "text/html",
            ".xml" => "text/xml",
            ".wasm" => "application/wasm",

            // Application content types
            ".atom" => "application/atom+xml",
            ".fastsoap" => "application/fastsoap",
            ".gzip" => "application/gzip",
            ".json" => "application/json",
            ".map" => "application/json",
            ".pdf" => "application/pdf",
            ".ps" => "application/postscript",
            ".soap" => "application/soap+xml",
            ".sql" => "application/sql",
            ".xslt" => "application/xslt+xml",
            ".zip" => "application/zip",
            ".zlib" => "application/zlib",

            // Audio content types
            ".aac" => "audio/aac",
            ".ac3" => "audio/ac3",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",

            // Font content types
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",

            // Image content types
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".emf" => "image/emf",
            ".gif" => "image/gif",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".jpm" => "image/jpm",
            ".jpx" => "image/jpx",
            ".jrx" => "image/jrx",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".tiff" => "image/tiff",
            ".wmf" => "image/wmf",
            ".ico" => "image/x-icon",

            // Message content types
            ".http" => "message/http",
            ".s-http" => "message/s-http",

            // Model content types
            ".mesh" => "model/mesh",
            ".vrml" => "model/vrml",

            // Text content types
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".plain" => "text/plain",
            ".richtext" => "text/richtext",
            ".rtf" => "text/rtf",
            ".rtx" => "text/rtx",
            ".sgml" => "text/sgml",
            ".strings" => "text/strings",
            ".url" => "text/uri-list",

            // Video content types
            ".H264" => "video/H264",
            ".H265" => "video/H265",
            ".mp4" => "video/mp4",
            ".mpeg" => "video/mpeg",
            ".raw" => "video/raw",

            _ => "application/octet-stream"
        };

        /// <summary>
        /// Add Header
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public HttpResponse AddHeader(string name, string value) {
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) {
                _hasCustomContentLength = true;
            }
            if (_headerCount == _headers.Length) {
                Array.Resize(ref _headers, _headers.Length * 2);
            }
            _headers[_headerCount++] = new HeaderEntry(name, value);
            return this;
        }

        /// <summary>
        /// Body from byte[] (borrowed stable, array-backed)
        /// </summary>
        /// <param name="body"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse Body(byte[] body, string? contentType = "application/octet-stream") {
            return Body(new ArraySegment<byte>(body, 0, body.Length), contentType);
        }

        /// <summary>
        /// Body from ArraySegment (borrowed stable, array-backed, supports offset/len)
        /// </summary>
        /// <param name="body"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse Body(ArraySegment<byte> body, string? contentType = "application/octet-stream") {
            DisposeBody();

            _bodyKind = BodyKind.Segment;
            _bodyArray = body.Array;
            _bodyOffset = body.Offset;
            _bodyLength = body.Count;

            _bodyMemory = default;
            _bodyOwner = null;

            if (contentType is not null) {
                _contentType = contentType;
            }
            return this;
        }

        /// <summary>
        /// Body from ReadOnlyMemory (borrowed stable if no owner provided)
        /// If array-backed => store as Segment immediately
        /// If not array-backed => store as Memory
        /// </summary>
        /// <param name="body"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse Body(ReadOnlyMemory<byte> body, string? contentType = "application/octet-stream") {
            DisposeBody();

            // is body an underlying segment ?
            if (MemoryMarshal.TryGetArray(body, out ArraySegment<byte> seg)) {
                _bodyKind = BodyKind.Segment;
                _bodyArray = seg.Array;
                _bodyOffset = seg.Offset;
                _bodyLength = seg.Count;

                _bodyMemory = default;
            }
            else {
                _bodyKind = BodyKind.Memory;
                _bodyMemory = body;

                _bodyArray = null;
                _bodyOffset = 0;
                _bodyLength = 0;
            }

            _bodyOwner = null;

            if (contentType is not null) {
                _contentType = contentType;
            }
            return this;
        }

        /// <summary>
        /// Body with explicit owner (zero-copy + safe lifetime)
        /// </summary>
        /// <param name="body"></param>
        /// <param name="owner"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse Body(ReadOnlyMemory<byte> body, IDisposable owner, string? contentType = "application/octet-stream") {
            DisposeBody();

            // is body an underlying segment ?
            if (MemoryMarshal.TryGetArray(body, out ArraySegment<byte> seg)) {
                _bodyKind = BodyKind.Segment;
                _bodyArray = seg.Array;
                _bodyOffset = seg.Offset;
                _bodyLength = seg.Count;

                _bodyMemory = default;
            }
            else {
                _bodyKind = BodyKind.Memory;
                _bodyMemory = body;

                _bodyArray = null;
                _bodyOffset = 0;
                _bodyLength = 0;
            }

            _bodyOwner = owner;

            if (contentType is not null) {
                _contentType = contentType;
            }
            return this;
        }

        /// <summary>
        /// Set UTF-8 text Body (owned pooled buffer)
        /// </summary>
        /// <param name="body"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse Text(string body, string contentType = "text/plain; charset=utf-8") {
            DisposeBody();
            _contentType = contentType;

            int maxBytes = Utf8NoBom.GetMaxByteCount(body.Length);
            byte[] buf = _bufferPool.Rent(maxBytes);
            int len = Utf8NoBom.GetBytes(body.AsSpan(), buf.AsSpan());

            _bodyKind = BodyKind.OwnedBuffer;
            _bodyArray = buf;
            _bodyOffset = 0;
            _bodyLength = len;

            return this;
        }

        /// <summary>
        /// Set JSON Body serialized into pooled buffer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse Json<T>(T value, string contentType = "application/json; charset=utf-8") {
            DisposeBody();
            _contentType = contentType;

            PooledBufferWriter writer = new(_bufferPool);

            using (Utf8JsonWriter jsonWriter = new(writer, new JsonWriterOptions { SkipValidation = true, Indented = false })) {
                JsonSerializer.Serialize(jsonWriter, value);
                jsonWriter.Flush();
            }

            _ownedBodyWriter = writer;
            _bodyKind = BodyKind.OwnedWriter;
            _bodyArray = writer.Buffer;
            _bodyOffset = 0;
            _bodyLength = writer.Length;

            return this;
        }

        /// <summary>
        /// Send the response now
        /// </summary>
        /// <returns></returns>
        public async ValueTask SendAsync() {
            if (_sent) {
                return;
            }
            _sent = true;

            // reserve (depending on body kind): body length or a ReadOnlyMemory 
            ReadOnlyMemory<byte> bodyMem = ReadOnlyMemory<byte>.Empty;
            int bodyLength = 0;

            switch (_bodyKind) {
                case BodyKind.None:
                    bodyMem = ReadOnlyMemory<byte>.Empty;
                    bodyLength = 0;
                    break;

                case BodyKind.Segment:
                case BodyKind.OwnedBuffer:
                    if (_bodyArray is not null && _bodyLength > 0) {
                        bodyMem = _bodyArray.AsMemory(_bodyOffset, _bodyLength);
                        bodyLength = _bodyLength;
                    }
                    break;

                case BodyKind.OwnedWriter:
                    if (_ownedBodyWriter is not null && _ownedBodyWriter.Length > 0) {
                        bodyMem = _ownedBodyWriter.Buffer.AsMemory(0, _ownedBodyWriter.Length);
                        bodyLength = _ownedBodyWriter.Length;
                    }
                    break;

                case BodyKind.Memory:
                    bodyMem = _bodyMemory;
                    bodyLength = bodyMem.Length;
                    break;
            }

            PooledBufferWriter? headerWriter = null;
            try {
                headerWriter = new PooledBufferWriter(_bufferPool, initialSize: 512);

                // Status line: HTTP/1.1 <code> <text>\r\n
                WriteBytes(headerWriter, H_HTTP11);
                WriteIntAscii(headerWriter, _statusCode);
                WriteAscii(headerWriter, " ");
                WriteAscii(headerWriter, _statusText);
                WriteCRLF(headerWriter);

                // Content-Length
                if (!_hasCustomContentLength) {
                    WriteBytes(headerWriter, H_CL);
                    WriteIntAscii(headerWriter, bodyLength);
                    WriteCRLF(headerWriter);
                }

                // Content-Type (only if set)
                if (!string.IsNullOrEmpty(_contentType)) {
                    WriteBytes(headerWriter, H_CT);
                    WriteAscii(headerWriter, _contentType!);
                    WriteCRLF(headerWriter);
                }

                // Connection (session decides keep-alive/close)
                WriteBytes(headerWriter, H_CONN);
                WriteBytes(headerWriter, _session.CloseAfterResponse ? H_CONN_CLOSE : H_CONN_KA);
                WriteCRLF(headerWriter);

                // Set-Cookie headers (multi allowed)
                WriteCookie(headerWriter);

                // custom headers
                for (int i = 0; i < _headerCount; i++) {
                    ref readonly HeaderEntry h = ref _headers[i];
                    if (string.IsNullOrEmpty(h.Name)) {
                        continue;
                    }
                    WriteAscii(headerWriter, h.Name);
                    WriteBytes(headerWriter, COLON_SP);
                    WriteAscii(headerWriter, h.Value ?? string.Empty);
                    WriteCRLF(headerWriter);
                }

                // end headers
                WriteCRLF(headerWriter);

                ArraySegment<byte> headerSeg = new ArraySegment<byte>(headerWriter.Buffer, 0, headerWriter.Length);

                // SEND STRATEGY
                if (bodyLength == 0) {
                    await _session.SendAsync(headerSeg).ConfigureAwait(false);
                }
                else if (_bodyKind == BodyKind.Segment || _bodyKind == BodyKind.OwnedBuffer || _bodyKind == BodyKind.OwnedWriter) {
                    // array-backed body => 1 socket send (exception for https)
                    ArraySegment<byte> bodySeg = new ArraySegment<byte>(_bodyArray!, _bodyOffset, _bodyLength);
                    await _session.SendAsync(headerSeg, bodySeg).ConfigureAwait(false);
                }
                else {
                    // non array-backed => 2 socket sends but zero-copy
                    await _session.SendAsync(headerSeg).ConfigureAwait(false);
                    await _session.SendAsync(bodyMem).ConfigureAwait(false);
                }
            }
            finally {
                headerWriter?.Dispose();
                DisposeBody(); // disposes owner if any + returns owned buffers
            }
        }

        #region cookies

        /// <summary>
        /// Array of Cookies
        /// </summary>
        private CookieEntry[]? _cookies;

        /// <summary>
        /// Real number of cookies write
        /// </summary>
        private int _cookieCount;

        /// <summary>
        /// Set a Cookie
        /// So tell client to create a cookie on its side
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        /// <example>
        /// SetCookie(
        ///     "sid",
        ///     Guid.NewGuid().ToString(),
        ///     new HttpResponse.CookieOptions(
        ///         path: "/",
        ///         maxAgeSeconds: 60 * 60 * 24 * 7,
        ///         secure: true,
        ///         httpOnly: true,
        ///         sameSite: HttpResponse.SameSiteMode.Lax
        /// ));
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse SetCookie(string name, string value, in CookieOptions options = default) {
            if (_cookies is null) {
                _cookies = new CookieEntry[2];
            }
            else if (_cookieCount == _cookies.Length) {
                Array.Resize(ref _cookies, _cookies.Length * 2);
            }
            _cookies[_cookieCount++] = new CookieEntry(name, value, options);
            return this;
        }

        /// <summary>
        /// Delete a Cookie
        /// So tell client to delete the cookie on its side
        /// </summary>
        /// <param name="name"></param>
        /// <param name="path"></param>
        /// <param name="domain"></param>
        /// <returns></returns>
        /// <example>
        /// DeleteCookie("sid");
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse DeleteCookie(string name, string? path = "/", string? domain = null) {
            // Max-Age=0 + Expires=Unix epoch (compat)
            CookieOptions opts = new(
                path: path,
                domain: domain,
                maxAgeSeconds: 0,
                expires: DateTimeOffset.UnixEpoch,
                secure: false,
                httpOnly: true,
                sameSite: SameSiteMode.Unspecified
            );
            return SetCookie(name, string.Empty, in opts);
        }

        /// <summary>
        /// Clear all Cookies
        /// clear cookies here, so no cookie will be written in the response
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse ClearCookies() {
            _cookieCount = 0;
            return this;
        }

        #region cookie structure

        /// <summary>
        /// Cookie SameSite Mode
        /// </summary>
        public enum SameSiteMode : byte {
            Unspecified = 0,
            Lax = 1,
            Strict = 2,
            None = 3
        }

        /// <summary>
        /// Cookie Options
        /// </summary>
        public readonly struct CookieOptions {
            public readonly string? Path;
            public readonly string? Domain;

            public readonly int MaxAge;          // seconds
            public readonly bool HasMaxAge;

            public readonly DateTimeOffset Expires;
            public readonly bool HasExpires;

            public readonly bool Secure;
            public readonly bool HttpOnly;

            public readonly SameSiteMode SameSite;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="path"></param>
            /// <param name="domain"></param>
            /// <param name="maxAgeSeconds"></param>
            /// <param name="expires"></param>
            /// <param name="secure"></param>
            /// <param name="httpOnly"></param>
            /// <param name="sameSite"></param>
            public CookieOptions(
                string? path = "/",
                string? domain = null,
                int? maxAgeSeconds = null,
                DateTimeOffset? expires = null,
                bool secure = false,
                bool httpOnly = true,
                SameSiteMode sameSite = SameSiteMode.Unspecified
            ) {
                Path = path;
                Domain = domain;

                if (maxAgeSeconds.HasValue) {
                    MaxAge = maxAgeSeconds.Value;
                    HasMaxAge = true;
                }
                else {
                    MaxAge = 0;
                    HasMaxAge = false;
                }

                if (expires.HasValue) {
                    Expires = expires.Value;
                    HasExpires = true;
                }
                else {
                    Expires = default;
                    HasExpires = false;
                }

                Secure = secure;
                HttpOnly = httpOnly;
                SameSite = sameSite;
            }
        }

        /// <summary>
        /// Cookie Entry
        /// </summary>
        private readonly struct CookieEntry {
            public readonly string Name;
            public readonly string Value;
            public readonly CookieOptions Options;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="name"></param>
            /// <param name="value"></param>
            /// <param name="options"></param>
            public CookieEntry(string name, string value, CookieOptions options) {
                Name = name;
                Value = value;
                Options = options;
            }
        }

        #endregion cookie structure

        #endregion cookies

        #region Dispose

        /// <summary>
        /// Reset response for next request (keep arrays allocated)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() {
            _statusCode = 200;
            _statusText = "OK";
            _contentType = null;

            _headerCount = 0;
            _hasCustomContentLength = false;

            DisposeBody();

            _cookieCount = 0;

            _sent = false;
        }

        /// <summary>
        /// Dispose Body
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DisposeBody() {
            // dispose explicit owner
            if (_bodyOwner is not null) {
                _bodyOwner.Dispose();
                _bodyOwner = null;
            }

            // dispose writer (returns its pooled array)
            if (_ownedBodyWriter is not null) {
                _ownedBodyWriter.Dispose();
                _ownedBodyWriter = null;
            }

            // return pooled buffer only if we own it
            if (_bodyKind == BodyKind.OwnedBuffer && _bodyArray is not null) {
                _bufferPool.Return(_bodyArray);
            }

            _bodyKind = BodyKind.None;
            _bodyMemory = ReadOnlyMemory<byte>.Empty;
            _bodyArray = null;
            _bodyOffset = 0;
            _bodyLength = 0;
        }

        #endregion Dispose

        #region response helpers

        private static readonly Encoding Ascii = Encoding.ASCII;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static ReadOnlySpan<byte> H_HTTP11 => "HTTP/1.1 "u8;
        private static ReadOnlySpan<byte> H_CL => "Content-Length: "u8;
        private static ReadOnlySpan<byte> H_CT => "Content-Type: "u8;
        private static ReadOnlySpan<byte> H_CONN => "Connection: "u8;
        private static ReadOnlySpan<byte> H_CONN_CLOSE => "close"u8;
        private static ReadOnlySpan<byte> H_CONN_KA => "keep-alive"u8;

        private static readonly byte[] CRLF = new byte[] { (byte)'\r', (byte)'\n' };
        private static readonly byte[] COLON_SP = new byte[] { (byte)':', (byte)' ' };

        /// <summary>
        /// Write CRLF in PoolBuffer
        /// </summary>
        /// <param name="w"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteCRLF(PooledBufferWriter w) => WriteBytes(w, CRLF);

        /// <summary>
        /// Write Bytes in PoolBuffer
        /// </summary>
        /// <param name="w"></param>
        /// <param name="bytes"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteBytes(PooledBufferWriter w, ReadOnlySpan<byte> bytes) {
            Span<byte> span = w.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            w.Advance(bytes.Length);
        }

        /// <summary>
        /// Write Ascii in PoolBuffer
        /// </summary>
        /// <param name="w"></param>
        /// <param name="s"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteAscii(PooledBufferWriter w, string s) {
            if (string.IsNullOrEmpty(s)) {
                return;
            }
            int max = Ascii.GetMaxByteCount(s.Length);
            Span<byte> span = w.GetSpan(max);
            int len = Ascii.GetBytes(s.AsSpan(), span);
            w.Advance(len);
        }

        /// <summary>
        /// Write Int in PoolBuffer
        /// </summary>
        /// <param name="w"></param>
        /// <param name="value"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteIntAscii(PooledBufferWriter w, int value) {
            Span<char> tmp = stackalloc char[16];
            if (!value.TryFormat(tmp, out int chars)) {
                // shouldn't happen
                WriteAscii(w, value.ToString());
                return;
            }

            // digits are ASCII-safe
            Span<byte> dst = w.GetSpan(chars);
            for (int i = 0; i < chars; i++) {
                dst[i] = (byte)tmp[i];
            }
            w.Advance(chars);
        }

        /// <summary>
        /// Kind of Body memory
        /// </summary>
        private enum BodyKind : byte {
            None = 0,
            Memory = 1,
            OwnedBuffer = 2,
            OwnedWriter = 3,
            Segment = 4
        }

        #endregion response helpers

        #region cookie helpers

        private static readonly byte[] EQ = new byte[] { (byte)'=' };
        private static readonly byte[] SEMI_SP = new byte[] { (byte)';', (byte)' ' };

        // "Set-Cookie: "
        private const string SET_COOKIE = "Set-Cookie: ";

        // attribute names
        private const string ATTR_PATH = "Path";
        private const string ATTR_DOMAIN = "Domain";
        private const string ATTR_MAXAGE = "Max-Age";
        private const string ATTR_EXPIRES = "Expires";
        private const string ATTR_SECURE = "Secure";
        private const string ATTR_HTTPONLY = "HttpOnly";
        private const string ATTR_SAMESITE = "SameSite";

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteRfc1123DateAscii(PooledBufferWriter w, DateTimeOffset dto) {
            // RFC1123 / IMF-fixdate, always UTC
            Span<char> tmp = stackalloc char[32]; // "ddd, dd MMM yyyy HH:mm:ss GMT" = 29
            DateTime utc = dto.UtcDateTime;
            if (!utc.TryFormat(tmp, out int chars, "r".AsSpan())) {
                // very unlikely fallback
                WriteAscii(w, utc.ToString("r", System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            Span<byte> dst = w.GetSpan(chars);
            for (int i = 0; i < chars; i++) {
                dst[i] = (byte)tmp[i]; // ASCII-safe
            }
            w.Advance(chars);
        }

        /// <summary>
        /// Write Cookie
        /// </summary>
        /// <param name="headerWriter"></param>
        private void WriteCookie(PooledBufferWriter headerWriter) {
            if (_cookieCount == 0) {
                return;
            }
            CookieEntry[]? cookies = _cookies;
            if (cookies == null) {
                return;
            }

            for (int i = 0; i < _cookieCount; i++) {
                ref readonly var c = ref cookies[i];
                if (string.IsNullOrEmpty(c.Name)) {
                    continue;
                }

                WriteAscii(headerWriter, SET_COOKIE);

                // name=value
                WriteAscii(headerWriter, c.Name);
                WriteBytes(headerWriter, EQ);
                WriteAscii(headerWriter, c.Value ?? string.Empty);

                // attributes
                ref readonly var o = ref Unsafe.AsRef(in c.Options);

                if (!string.IsNullOrEmpty(o.Path)) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_PATH);
                    WriteBytes(headerWriter, EQ);
                    WriteAscii(headerWriter, o.Path);
                }

                if (!string.IsNullOrEmpty(o.Domain)) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_DOMAIN);
                    WriteBytes(headerWriter, EQ);
                    WriteAscii(headerWriter, o.Domain);
                }

                if (o.HasMaxAge) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_MAXAGE);
                    WriteBytes(headerWriter, EQ);
                    WriteIntAscii(headerWriter, o.MaxAge);
                }

                if (o.HasExpires) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_EXPIRES);
                    WriteBytes(headerWriter, EQ);
                    WriteRfc1123DateAscii(headerWriter, o.Expires);
                }

                if (o.Secure) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_SECURE);
                }

                if (o.HttpOnly) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_HTTPONLY);
                }

                if (o.SameSite != SameSiteMode.Unspecified) {
                    WriteBytes(headerWriter, SEMI_SP);
                    WriteAscii(headerWriter, ATTR_SAMESITE);
                    WriteBytes(headerWriter, EQ);
                    switch (o.SameSite) {
                        case SameSiteMode.Lax:
                            WriteAscii(headerWriter, "Lax");
                            break;
                        case SameSiteMode.Strict:
                            WriteAscii(headerWriter, "Strict");
                            break;
                        case SameSiteMode.None:
                            WriteAscii(headerWriter, "None");
                            break;
                    }
                }

                WriteCRLF(headerWriter);
            }
        }

        #endregion cookie helpers

    }
}
