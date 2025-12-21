using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SimpleW.Buffers;


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

        /// <summary>
        /// Current compression mode (default: Auto)
        /// </summary>
        private ResponseCompressionMode _compressionMode = ResponseCompressionMode.Auto;

        /// <summary>
        /// Minimum body size (bytes) before auto-compress kicks in (default: 512)
        /// </summary>
        private int _compressionMinSize = 512;

        /// <summary>
        /// Compression level (default: Fastest)
        /// </summary>
        private CompressionLevel _compressionLevel = CompressionLevel.Fastest;

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
        /// for BodyKind.File
        /// </summary>
        private string? _filePath;
        private long _fileLength;

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
            _filePath = null;
            _fileLength = 0;

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
        /// Set File Body
        /// </summary>
        /// <param name="path"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public HttpResponse File(string path, string? contentType = null) {
            return File(new FileInfo(path), contentType);
        }

        /// <summary>
        /// Set File Body from FileInfo (avoids re-stat / allows caller to pre-validate)
        /// </summary>
        /// <param name="fi"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public HttpResponse File(FileInfo fi, string? contentType = null) {
            ArgumentNullException.ThrowIfNull(fi);
            DisposeBody();

            _bodyKind = BodyKind.File;

            _filePath = fi.FullName; // set fullname !
            _fileLength = fi.Length; // can throw if file does not exists or cannot be access

            _contentType = contentType ?? DefaultContentType(fi.Extension);

            return this;
        }

        /// <summary>
        /// Configure compression policy for this response
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="minSize"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse Compression(ResponseCompressionMode mode, int? minSize = null, CompressionLevel? level = null) {
            _compressionMode = mode;
            if (minSize.HasValue) {
                _compressionMinSize = (minSize.Value < 0 ? 0 : minSize.Value);
            }
            if (level.HasValue) {
                _compressionLevel = level.Value;
            }
            return this;
        }

        /// <summary>
        /// Convenience: disable compression for this response
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse NoCompression() => Compression(ResponseCompressionMode.Disabled);

        //
        // SEND
        //

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
            long bodyLengthLong = 0;

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

                case BodyKind.File:
                    bodyLengthLong = _fileLength;
                    // bodyMem reste vide, on stream après
                    break;
            }

            // negotiate/compress (optional)
            PooledBufferWriter? compressedWriter = null;
            ArraySegment<byte> compressedSeg = default;
            NegotiatedEncoding negotiated = NegotiatedEncoding.None;

            bool canCompressBody = bodyLength > 0
                                   && _bodyKind != BodyKind.File
                                   && !_hasCustomContentLength
                                   && _statusCode != 204
                                   && _statusCode != 304
                                   && !HasHeaderIgnoreCase("Content-Encoding")
                                   && IsCompressibleContentType(_contentType);

            if (_compressionMode != ResponseCompressionMode.Disabled && canCompressBody) {
                bool wantCompress = _compressionMode switch {
                    ResponseCompressionMode.Auto => (bodyLength >= _compressionMinSize),
                    ResponseCompressionMode.ForceGzip => true,
                    ResponseCompressionMode.ForceDeflate => true,
                    _ => false
                };

                if (wantCompress) {
                    bool allowGzip = _compressionMode != ResponseCompressionMode.ForceDeflate;
                    bool allowDeflate = _compressionMode != ResponseCompressionMode.ForceGzip;
                    negotiated = NegotiateEncoding(_session.Request.Headers.AcceptEncoding, allowGzip, allowDeflate);

                    if (negotiated != NegotiatedEncoding.None) {
                        compressedWriter = CompressToPooledWriter(_bufferPool, bodyMem, negotiated, _compressionLevel);
                        compressedSeg = new ArraySegment<byte>(compressedWriter.Buffer, 0, compressedWriter.Length);

                        bool forced = _compressionMode == ResponseCompressionMode.ForceGzip || _compressionMode == ResponseCompressionMode.ForceDeflate;
                        if (!forced && compressedWriter.Length >= bodyLength) {
                            compressedWriter.Dispose();
                            compressedWriter = null;
                            negotiated = NegotiatedEncoding.None;
                        }
                    }
                }
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
                int finalBodyLength = (negotiated != NegotiatedEncoding.None && compressedWriter != null) ? compressedWriter.Length : bodyLength;
                if (!_hasCustomContentLength) {
                    WriteBytes(headerWriter, H_CL);
                    if (_bodyKind == BodyKind.File) {
                        WriteLongAscii(headerWriter, bodyLengthLong);
                    }
                    else {
                        WriteIntAscii(headerWriter, finalBodyLength);
                    }
                    WriteCRLF(headerWriter);
                }

                // Content-Type (only if set)
                if (!string.IsNullOrEmpty(_contentType)) {
                    WriteBytes(headerWriter, H_CT);
                    WriteAscii(headerWriter, _contentType!);
                    WriteCRLF(headerWriter);
                }

                // Content-Encoding + Vary (only if compressed)
                if (negotiated != NegotiatedEncoding.None && compressedWriter != null) {
                    WriteBytes(headerWriter, H_CE);
                    WriteBytes(headerWriter, negotiated == NegotiatedEncoding.Gzip ? H_GZIP : H_DEFLATE);
                    WriteCRLF(headerWriter);

                    // Vary: Accept-Encoding (avoid duplicates if caller already set it)
                    if (!HasHeaderValueTokenIgnoreCase("Vary", "Accept-Encoding")) {
                        WriteBytes(headerWriter, H_VARY);
                        WriteAscii(headerWriter, "Accept-Encoding");
                        WriteCRLF(headerWriter);
                    }
                }

                // Connection (session decides keep-alive/close)
                WriteBytes(headerWriter, H_CONN);
                WriteBytes(headerWriter, _session.CloseAfterResponse ? H_CONN_CLOSE : H_CONN_KA);
                WriteCRLF(headerWriter);

                // Set-Cookie headers (multi allowed)
                WriteCookie(headerWriter);

                // custom headers
                for (int i = 0; i < _headerCount; i++) {
#if NET9_0_OR_GREATER
                    ref readonly HeaderEntry h = ref _headers[i];
#else
                    HeaderEntry h = _headers[i];
#endif
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
                if (_bodyKind == BodyKind.File) {
                    // header
                    await _session.SendAsync(headerSeg).ConfigureAwait(false);
                    // stream body file
                    const int FileChunkSize = 64 * 1024;
                    using var fs = new FileStream(
                        _filePath!,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: FileChunkSize,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan
                    );
                    byte[] buf = _bufferPool.Rent(FileChunkSize);
                    try {
                        while (true) {
                            int read = await fs.ReadAsync(buf.AsMemory(0, buf.Length)).ConfigureAwait(false);
                            if (read <= 0) {
                                break;
                            }
                            await _session.SendAsync(new ArraySegment<byte>(buf, 0, read)).ConfigureAwait(false);
                        }
                    }
                    finally {
                        _bufferPool.Return(buf);
                    }
                }
                else if (finalBodyLength == 0) {
                    await _session.SendAsync(headerSeg).ConfigureAwait(false);
                }
                else if (negotiated != NegotiatedEncoding.None && compressedWriter != null) {
                    // compressed payload is always array-backed
                    await _session.SendAsync(headerSeg, compressedSeg).ConfigureAwait(false);
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
                compressedWriter?.Dispose();
                headerWriter?.Dispose();
                DisposeBody(); // disposes owner if any + returns owned buffers
            }
        }

        #region alias

        /// <summary>
        /// Not Found 404
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        public HttpResponse NotFound(string? body = null) {
            DisposeBody();
            Status(404);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Internal Server Error 500
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        public HttpResponse InternalServerError(string? body = null) {
            DisposeBody();
            Status(500);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Redirect 302
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public HttpResponse Redirect(string url) {
            DisposeBody();
            Status(302);
            AddHeader("Location", url);
            return this;
        }

        /// <summary>
        /// Unauthorized 401
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        public HttpResponse Unauthorized(string? body = null) {
            DisposeBody();
            Status(401);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Forbidden 403
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        public HttpResponse Forbidden(string? body = null) {
            DisposeBody();
            Status(403);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Access 401/403
        /// </summary>
        /// <param name="isWebuser"></param>
        /// <returns></returns>
        public HttpResponse Access(bool isWebuser = false) {
            if (isWebuser) {
                return Forbidden();
            }
            return Unauthorized();
        }

        #endregion alias

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
            /// <summary>
            /// No SameSite field will be set, the client should follow its default cookie policy.
            /// </summary>
            Unspecified = 0,
            /// <summary>
            /// Indicates the client should send the cookie with "same-site" requests,
            /// and with "cross-site" top-level navigations.
            /// </summary>
            Lax = 1,
            /// <summary>
            /// Indicates the client should only send the cookie with "same-site" requests.
            /// </summary>
            Strict = 2,
            /// <summary>
            /// Indicates the client should disable same-site restrictions.
            /// When using this value, the cookie must also have the Secure property set to true.
            /// </summary>
            None = 3
        }

        /// <summary>
        /// Cookie Options
        /// </summary>
        public readonly struct CookieOptions {

            /// <summary>
            /// Path
            /// </summary>
            public readonly string? Path;

            /// <summary>
            /// Domaine
            /// </summary>
            public readonly string? Domain;

            /// <summary>
            /// MaxAge in seconds
            /// </summary>
            public readonly int MaxAge;
            /// <summary>
            /// HasMaxAge
            /// </summary>
            public readonly bool HasMaxAge;

            /// <summary>
            /// Expires
            /// </summary>
            public readonly DateTimeOffset Expires;
            /// <summary>
            /// HasExpires
            /// </summary>
            public readonly bool HasExpires;

            /// <summary>
            /// Secure
            /// </summary>
            public readonly bool Secure;

            /// <summary>
            /// HttpOnly
            /// </summary>
            public readonly bool HttpOnly;

            /// <summary>
            /// SameSite
            /// </summary>
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

            _compressionMode = ResponseCompressionMode.Auto;
            _compressionMinSize = 512;
            _compressionLevel = CompressionLevel.Fastest;

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
            _filePath = null;
            _fileLength = 0;
        }

        #endregion Dispose

        #region write helpers

        /// <summary>
        /// Ascii Encoding Alias
        /// </summary>
        private static readonly Encoding Ascii = Encoding.ASCII;
        /// <summary>
        /// UTF8 Encoding Alias
        /// </summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// "HTTP/1.1 "
        /// </summary>
        private static ReadOnlySpan<byte> H_HTTP11 => "HTTP/1.1 "u8;
        /// <summary>
        /// "Content-Length: "
        /// </summary>
        private static ReadOnlySpan<byte> H_CL => "Content-Length: "u8;
        /// <summary>
        /// "Content-Type: "
        /// </summary>
        private static ReadOnlySpan<byte> H_CT => "Content-Type: "u8;
        /// <summary>
        /// "Content-Encoding: "
        /// </summary>
        private static ReadOnlySpan<byte> H_CE => "Content-Encoding: "u8;
        /// <summary>
        /// "Vary: "
        /// </summary>
        private static ReadOnlySpan<byte> H_VARY => "Vary: "u8;
        /// <summary>
        /// "Connection: "
        /// </summary>
        private static ReadOnlySpan<byte> H_CONN => "Connection: "u8;
        /// <summary>
        /// "close"
        /// </summary>
        private static ReadOnlySpan<byte> H_CONN_CLOSE => "close"u8;
        /// <summary>
        /// "keep-alive"
        /// </summary>
        private static ReadOnlySpan<byte> H_CONN_KA => "keep-alive"u8;
        /// <summary>
        /// "gzip"
        /// </summary>
        private static ReadOnlySpan<byte> H_GZIP => "gzip"u8;
        /// <summary>
        /// "deflate"
        /// </summary>
        private static ReadOnlySpan<byte> H_DEFLATE => "deflate"u8;
        /// <summary>
        /// "Accept-Encoding"
        /// </summary>
        private static ReadOnlySpan<byte> H_ACCEPT_ENCODING => "Accept-Encoding"u8;

        /// <summary>
        /// Cariage Return Line Feed
        /// </summary>
        private static readonly byte[] CRLF = new byte[] { (byte)'\r', (byte)'\n' };

        /// <summary>
        /// Colon Separator
        /// </summary>
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
        /// Write Long in PoolBuffer
        /// </summary>
        /// <param name="w"></param>
        /// <param name="value"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLongAscii(PooledBufferWriter w, long value) {
            Span<char> tmp = stackalloc char[32];
            if (!value.TryFormat(tmp, out int chars)) {
                WriteAscii(w, value.ToString());
                return;
            }
            Span<byte> dst = w.GetSpan(chars);
            for (int i = 0; i < chars; i++) {
                dst[i] = (byte)tmp[i];
            }
            w.Advance(chars);
        }

        /// <summary>
        /// Kind of Body
        /// </summary>
        private enum BodyKind : byte {
            None = 0,
            Memory = 1,
            OwnedBuffer = 2,
            OwnedWriter = 3,
            Segment = 4,
            File = 5
        }

        #endregion write helpers

        #region compression write helpers

        /// <summary>
        /// Response compression mode
        /// </summary>
        public enum ResponseCompressionMode : byte {
            /// <summary>
            /// Choose automatically based on request Accept-Encoding and heuristics
            /// </summary>
            Auto = 0,
            /// <summary>
            /// Disable response compression
            /// </summary>
            Disabled = 1,
            /// <summary>
            /// Force gzip (if client allows it)
            /// </summary>
            ForceGzip = 2,
            /// <summary>
            /// Force deflate (if client allows it)
            /// </summary>
            ForceDeflate = 3
        }

        /// <summary>
        /// Negotiated Encoding
        /// </summary>
        private enum NegotiatedEncoding : byte { None = 0, Gzip = 1, Deflate = 2 }

        /// <summary>
        /// Return true if the current HttpResponse has header matching name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHeaderIgnoreCase(string name) {
            for (int i = 0; i < _headerCount; i++) {
#if NET9_0_OR_GREATER
                ref readonly HeaderEntry h = ref _headers[i];
#else
                HeaderEntry h = _headers[i];
#endif
                if (!string.IsNullOrEmpty(h.Name) && h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Return true if the current HttpResponse has header containing name
        /// </summary>
        /// <param name="headerName"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHeaderValueTokenIgnoreCase(string headerName, string token) {
            for (int i = 0; i < _headerCount; i++) {
#if NET9_0_OR_GREATER
                ref readonly HeaderEntry h = ref _headers[i];
#else
                HeaderEntry h = _headers[i];
#endif
                if (!string.IsNullOrEmpty(h.Name) && h.Name.Equals(headerName, StringComparison.OrdinalIgnoreCase)) {
                    string v = h.Value ?? string.Empty;
                    if (v.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Check if body can be compress depending on its content type
        /// </summary>
        /// <param name="contentType"></param>
        /// <returns></returns>
        private static bool IsCompressibleContentType(string? contentType) {
            if (string.IsNullOrEmpty(contentType)) {
                return true;
            }

            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                return false;

            if (contentType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("application/gzip", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("application/x-gzip", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("application/zlib", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("application/x-rar", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("application/x-7z", StringComparison.OrdinalIgnoreCase))
                return false;
            if (contentType.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Return the Negotiated Encoding depending on client accept encoding and server settings
        /// </summary>
        /// <param name="acceptEncoding"></param>
        /// <param name="allowGzip"></param>
        /// <param name="allowDeflate"></param>
        /// <returns></returns>
        private static NegotiatedEncoding NegotiateEncoding(string? acceptEncoding, bool allowGzip, bool allowDeflate) {
            if (string.IsNullOrEmpty(acceptEncoding)) {
                return NegotiatedEncoding.None;
            }

            float qGzip = -1f;
            float qDeflate = -1f;

            ReadOnlySpan<char> s = acceptEncoding.AsSpan();
            int i = 0;

            while (i < s.Length) {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == ',')) {
                    i++;
                }
                if (i >= s.Length) {
                    break;
                }

                int tokenStart = i;
                while (i < s.Length && s[i] != ',') {
                    i++;
                }
                ReadOnlySpan<char> item = s.Slice(tokenStart, i - tokenStart).Trim();
                if (item.IsEmpty) {
                    continue;
                }

                int semi = item.IndexOf(';');
                ReadOnlySpan<char> name = semi >= 0 ? item.Slice(0, semi).Trim() : item;
                ReadOnlySpan<char> parms = semi >= 0 ? item.Slice(semi + 1) : ReadOnlySpan<char>.Empty;

                float q = 1f;
                if (!parms.IsEmpty) {
                    int qi = parms.IndexOf("q=".AsSpan(), StringComparison.OrdinalIgnoreCase);
                    if (qi >= 0) {
                        ReadOnlySpan<char> qspan = parms.Slice(qi + 2).Trim();
                        int end = qspan.IndexOf(';');
                        if (end >= 0)
                            qspan = qspan.Slice(0, end);
                        if (float.TryParse(qspan, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out float parsed)) {
                            q = parsed;
                        }
                    }
                }

                if (name.Equals("gzip".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                    qGzip = q;
                }
                else if (name.Equals("deflate".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                    qDeflate = q;
                }
            }

            if (!allowGzip) {
                qGzip = -1f;
            }
            if (!allowDeflate) {
                qDeflate = -1f;
            }

            if (qGzip <= 0f && qDeflate <= 0f) {
                return NegotiatedEncoding.None;
            }

            if (qGzip >= qDeflate) {
                return qGzip > 0f ? NegotiatedEncoding.Gzip : NegotiatedEncoding.None;
            }
            return qDeflate > 0f ? NegotiatedEncoding.Deflate : NegotiatedEncoding.None;
        }

        /// <summary>
        /// CompressTo pooled writer
        /// </summary>
        /// <param name="pool"></param>
        /// <param name="input"></param>
        /// <param name="encoding"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        private static PooledBufferWriter CompressToPooledWriter(ArrayPool<byte> pool, ReadOnlyMemory<byte> input, NegotiatedEncoding encoding, CompressionLevel level) {
            int init = input.Length <= 0 ? 256 : Math.Min(Math.Max(256, input.Length / 2), 64 * 1024);
            PooledBufferWriter output = new(pool, initialSize: init);

            try {
                using Stream bw = new BufferWriterStream(output);
                Stream compressor = encoding switch {
                    NegotiatedEncoding.Gzip => new GZipStream(bw, level, leaveOpen: true),
                    NegotiatedEncoding.Deflate => new DeflateStream(bw, level, leaveOpen: true),
                    _ => throw new ArgumentOutOfRangeException(nameof(encoding))
                };

                using (compressor) {
                    compressor.Write(input.Span);
                    compressor.Flush();
                }

                return output;
            }
            catch {
                output.Dispose();
                throw;
            }
        }

        #endregion compression write helpers

        #region cookie write helpers

        /// <summary>
        /// Equals
        /// </summary>
        private static readonly byte[] EQ = new byte[] { (byte)'=' };
        /// <summary>
        /// Semi-Colunm Separator
        /// </summary>
        private static readonly byte[] SEMI_SP = new byte[] { (byte)';', (byte)' ' };

        /// <summary>
        /// "Set-Cookie: "
        /// </summary>
        private const string SET_COOKIE = "Set-Cookie: ";
        /// <summary>
        /// "Path"
        /// </summary>
        private const string ATTR_PATH = "Path";
        /// <summary>
        /// "Domain"
        /// </summary>
        private const string ATTR_DOMAIN = "Domain";
        /// <summary>
        /// "Max-Age"
        /// </summary>
        private const string ATTR_MAXAGE = "Max-Age";
        /// <summary>
        /// "Expires"
        /// </summary>
        private const string ATTR_EXPIRES = "Expires";
        /// <summary>
        /// "Secure"
        /// </summary>
        private const string ATTR_SECURE = "Secure";
        /// <summary>
        /// "HttpOnly"
        /// </summary>
        private const string ATTR_HTTPONLY = "HttpOnly";
        /// <summary>
        /// "SameSite"
        /// </summary>
        private const string ATTR_SAMESITE = "SameSite";

        /// <summary>
        /// Write RFC1123 date format
        /// </summary>
        /// <param name="w"></param>
        /// <param name="dto"></param>
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

        #endregion cookie write helpers

    }

}
