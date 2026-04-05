using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SimpleW.Buffers;


namespace SimpleW {

    /// <summary>
    /// Builds an HTTP response.
    /// Owns the status line, headers, and body, and can send the response through HttpSession.SendAsync().
    /// </summary>
    public sealed class HttpResponse {

        #region internal properties

        /// <summary>
        /// The current HTTP session.
        /// </summary>
        private readonly HttpSession _session;

        /// <summary>
        /// The shared byte array pool.
        /// </summary>
        private readonly ArrayPool<byte> _bufferPool;

        /// <summary>
        /// Indicates whether the response has already been sent.
        /// </summary>
        private bool _sent;

        /// <summary>
        /// The HTTP status code.
        /// </summary>
        private int _statusCode;

        /// <summary>
        /// The HTTP status text.
        /// </summary>
        private string _statusText;

        /// <summary>
        /// The response content type.
        /// </summary>
        private string? _contentType;

        /// <summary>
        /// The response headers.
        /// </summary>
        private HeaderEntry[] _headers;

        /// <summary>
        /// The number of headers currently stored.
        /// </summary>
        private int _headerCount;

        /// <summary>
        /// Indicates whether the user explicitly set Content-Length via AddHeader,
        /// so it must not be generated automatically.
        /// </summary>
        private long? _customContentLength;

        /// <summary>
        /// Indicates whether Content-Length generation is disabled.
        /// </summary>
        private bool _suppressContentLength;

        /// <summary>
        /// The Connection header value.
        /// </summary>
        private string? _connection;

        /// <summary>
        /// Current compression mode (default: Auto)
        /// </summary>
        private ResponseCompressionMode _compressionMode = ResponseCompressionMode.Auto;

        /// <summary>
        /// The minimum body size, in bytes, before automatic compression is applied. Defaults to 512.
        /// </summary>
        private int _compressionMinSize = 512;

        /// <summary>
        /// The compression level. Defaults to Fastest.
        /// </summary>
        private CompressionLevel _compressionLevel = CompressionLevel.Fastest;

        //
        // BODY
        //

        /// <summary>
        /// The current body storage kind.
        /// </summary>
        private BodyKind _bodyKind;

        /// <summary>
        /// Stores the body when BodyKind is Memory.
        /// </summary>
        private ReadOnlyMemory<byte> _bodyMemory;

        /// <summary>
        /// Stores the body when BodyKind is Segment or OwnedBuffer.
        /// </summary>
        private byte[]? _bodyArray;
        private int _bodyOffset;
        private int _bodyLength;

        /// <summary>
        /// Stores the file path and length when BodyKind is File.
        /// </summary>
        private string? _filePath;
        private long _fileLength;

        /// <summary>
        /// Stores range information when file range support is enabled.
        /// </summary>
        private bool _fileRangeEnabled;
        private long _fileRangeStart;
        private long _fileRangeLength;

        /// <summary>
        /// Owns a pooled body writer backed by ArrayPool.
        /// </summary>
        private PooledBufferWriter? _ownedBodyWriter;

        /// <summary>
        /// Optional owner used to manage the body lifetime.
        /// </summary>
        private IDisposable? _bodyOwner;

        #endregion internal properties

        #region exposed properties

        /// <summary>
        /// Gets the current HTTP status code.
        /// </summary>
        public int StatusCode => _statusCode;

        /// <summary>
        /// Gets or sets the total number of bytes sent.
        /// </summary>
        public long BytesSent { get; set; }

        /// <summary>
        /// Gets whether the response has already been sent.
        /// </summary>
        public bool Sent => _sent;

        /// <summary>
        /// Gets the Connection header value.
        /// </summary>
        public string? Connection => _connection;

        #endregion exposed properties

        /// <summary>
        /// Initializes a new HTTP response.
        /// </summary>
        /// <param name="session">The current HTTP session.</param>
        /// <param name="bufferPool">The byte array pool used for temporary buffers.</param>
        public HttpResponse(HttpSession session, ArrayPool<byte> bufferPool) {
            _session = session;
            _bufferPool = bufferPool;

            _statusCode = 200;
            _statusText = "OK";

            _headers = new HeaderEntry[8];
            _headerCount = 0;
            _customContentLength = null;

            _bodyKind = BodyKind.None;
            _bodyMemory = ReadOnlyMemory<byte>.Empty;
            _bodyArray = null;
            _bodyOffset = 0;
            _bodyLength = 0;

            _filePath = null;
            _fileLength = 0;
            _fileRangeEnabled = false;
            _fileRangeStart = 0;
            _fileRangeLength = 0;

            _cookieCount = 0;

            _sent = false;
        }

        #region manipulate (high level methods)

        /// <summary>
        /// Sets the response status code and optional status text.
        /// </summary>
        /// <param name="statusCode">The HTTP status code.</param>
        /// <param name="statusText">The optional status text. If null, the default text is used.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Status(int statusCode, string? statusText = null) {
            _statusCode = statusCode;
            _statusText = statusText ?? DefaultStatusText(statusCode);
            return this;
        }

        /// <summary>
        /// Gets the default status text for the specified HTTP status code.
        /// </summary>
        /// <param name="code">The HTTP status code.</param>
        /// <returns>The default status text.</returns>
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
        /// Sets the response content type.
        /// </summary>
        /// <param name="contentType">The content type to set.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse ContentType(string contentType) {
            _contentType = contentType;
            return this;
        }

        /// <summary>
        /// Sets the content type from a file extension (for example, ".html").
        /// </summary>
        /// <param name="extension">The file extension.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse ContextTypeFromExtension(string extension) {
            _contentType = DefaultContentType(extension);
            return this;
        }

        /// <summary>
        /// Gets the default content type for a file extension (for example, ".html").
        /// </summary>
        /// <param name="extension">The file extension.</param>
        /// <returns>The matching content type.</returns>
        public static string DefaultContentType(string extension) => extension switch {

            // web
            ".css" => "text/css",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".js" => "text/javascript",
            ".mjs" => "text/javascript",
            ".vue" => "text/html",
            ".wasm" => "application/wasm",
            ".xml" => "application/xml",

            // Application content types
            ".atom" => "application/atom+xml",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".fastsoap" => "application/fastsoap",
            ".gzip" => "application/gzip",
            ".json" => "application/json",
            ".map" => "application/json",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".pdf" => "application/pdf",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".ps" => "application/postscript",
            ".soap" => "application/soap+xml",
            ".sql" => "application/sql",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xslt" => "application/xslt+xml",
            ".zlib" => "application/zlib",

            // Archive content types
            ".7z" => "application/x-7z-compressed",
            ".rar" => "application/vnd.rar",
            ".tar" => "application/x-tar",
            ".zip" => "application/zip",

            // Audio content types
            ".aac" => "audio/aac",
            ".ac3" => "audio/ac3",
            ".flac" => "audio/flac",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",

            // Font content types
            ".otf" => "font/otf",
            ".ttf" => "font/ttf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",

            // Image content types
            ".bmp" => "image/bmp",
            ".emf" => "image/emf",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".jpeg" => "image/jpeg",
            ".jpg" => "image/jpeg",
            ".jpm" => "image/jpm",
            ".jpx" => "image/jpx",
            ".jrx" => "image/jrx",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            ".wmf" => "image/wmf",

            // Message content types
            ".http" => "message/http",
            ".s-http" => "message/s-http",

            // Model content types
            ".gltf" => "model/gltf+json",
            ".mesh" => "model/mesh",
            ".obj" => "model/obj",
            ".vrml" => "model/vrml",

            // Text content types
            ".csv" => "text/csv",
            ".log" => "text/plain",
            ".markdown" => "text/markdown",
            ".md" => "text/markdown",
            ".plain" => "text/plain",
            ".richtext" => "text/richtext",
            ".rtf" => "text/rtf",
            ".rtx" => "text/rtx",
            ".sgml" => "text/sgml",
            ".strings" => "text/strings",
            ".txt" => "text/plain",
            ".url" => "text/uri-list",
            ".yaml" => "text/yaml",
            ".yml" => "text/yaml",

            // Video content types
            ".H264" => "video/H264",
            ".H265" => "video/H265",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".mp4" => "video/mp4",
            ".mpeg" => "video/mpeg",
            ".raw" => "video/raw",
            ".webm" => "video/webm",

            _ => "application/octet-stream"
        };

        /// <summary>
        /// Adds a response header.
        /// </summary>
        /// <param name="name">The header name.</param>
        /// <param name="value">The header value.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse AddHeader(string name, string value) {
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase)) {
                if (!long.TryParse(value, out long cl) || cl < 0) {
                    throw new InvalidOperationException($"Invalid Custom Header Content-Length {value}.");
                }
                _customContentLength = cl;
                return this;
            }
            if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase)) {
                _contentType = value;
                return this;
            }
            if (string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)) {
                _connection = value;
                return this;
            }
            if (string.Equals(name, "Date", StringComparison.OrdinalIgnoreCase)) {
                return this;
            }
            if (_headerCount == _headers.Length) {
                Array.Resize(ref _headers, _headers.Length * 2);
            }
            _headers[_headerCount++] = new HeaderEntry(name, value);
            return this;
        }

        /// <summary>
        /// Sets the body from a byte array.
        /// The array is borrowed and must remain valid until the response is sent.
        /// </summary>
        /// <param name="body">The response body.</param>
        /// <param name="contentType">The optional content type.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Body(byte[] body, string? contentType = "application/octet-stream") {
            return Body(new ArraySegment<byte>(body, 0, body.Length), contentType);
        }

        /// <summary>
        /// Sets the body from an ArraySegment.
        /// The array is borrowed and must remain valid until the response is sent.
        /// </summary>
        /// <param name="body">The response body segment.</param>
        /// <param name="contentType">The optional content type.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Body(ArraySegment<byte> body, string? contentType = "application/octet-stream") {
            DisposeBody();

            _bodyKind = BodyKind.Segment;
            _bodyArray = body.Array;
            _bodyOffset = body.Offset;
            _bodyLength = body.Count;

            _bodyMemory = default;
            _bodyOwner = null;

            if (contentType != null) {
                _contentType = contentType;
            }
            return this;
        }

        /// <summary>
        /// Sets the body from ReadOnlyMemory.
        /// If the memory is array-backed, it is stored as a segment; otherwise, it is stored as memory.
        /// </summary>
        /// <param name="body">The response body.</param>
        /// <param name="contentType">The optional content type.</param>
        /// <returns>The current response instance.</returns>
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

            if (contentType != null) {
                _contentType = contentType;
            }
            return this;
        }

        /// <summary>
        /// Sets the body with an explicit owner to guarantee a safe lifetime without copying.
        /// </summary>
        /// <param name="body">The response body.</param>
        /// <param name="owner">The owner responsible for the body lifetime.</param>
        /// <param name="contentType">The optional content type.</param>
        /// <returns>The current response instance.</returns>
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

            if (contentType != null) {
                _contentType = contentType;
            }
            return this;
        }

        /// <summary>
        /// Sets a UTF-8 text body using an owned pooled buffer.
        /// </summary>
        /// <param name="body">The text body.</param>
        /// <param name="contentType">The content type to use.</param>
        /// <returns>The current response instance.</returns>
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
        /// Sets a UTF-8 HTML body using an owned pooled buffer.
        /// </summary>
        /// <param name="body">The HTML body.</param>
        /// <param name="contentType">The content type to use.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Html(string body, string contentType = "text/html; charset=utf-8") {
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
        /// Serializes a value as JSON into a pooled buffer.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="contentType">The content type to use.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Json<T>(T value, string contentType = "application/json; charset=utf-8") {
            DisposeBody();
            _contentType = contentType;

            PooledBufferWriter writer = new(_bufferPool);

            // use the JsonEngine defined in server/session
            _session.JsonEngine.SerializeUtf8(writer, value);

            _ownedBodyWriter = writer;
            _bodyKind = BodyKind.OwnedWriter;
            _bodyArray = writer.Buffer;
            _bodyOffset = 0;
            _bodyLength = writer.Length;

            return this;
        }

        /// <summary>
        /// Sets the response body from a file path.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="contentType">The optional content type.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse File(string path, string? contentType = null) {
            return File(new FileInfo(path), contentType);
        }

        /// <summary>
        /// Sets the response body from a FileInfo instance.
        /// Avoids an extra stat call and allows the caller to validate the file first.
        /// </summary>
        /// <param name="fi">The file information.</param>
        /// <param name="contentType">The optional content type.</param>
        /// <returns>The current response instance.</returns>
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
        /// Configures the compression policy for this response.
        /// </summary>
        /// <param name="mode">The compression mode.</param>
        /// <param name="minSize">The minimum body size required for automatic compression.</param>
        /// <param name="level">The compression level.</param>
        /// <returns>The current response instance.</returns>
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
        /// Disables compression for this response.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse NoCompression() => Compression(ResponseCompressionMode.Disabled);

        /// <summary>
        /// Removes the current response body.
        /// </summary>
        /// <returns>The current response instance.</returns>
        public HttpResponse RemoveBody() {
            DisposeBody();
            _customContentLength = null;
            _suppressContentLength = false;
            return this;
        }

        /// <summary>
        /// Prevents Content-Length from being written.
        /// </summary>
        /// <returns>The current response instance.</returns>
        public HttpResponse NoContentLength() {
            _customContentLength = null;
            _suppressContentLength = true;
            return this;
        }

        //
        // SEND
        //

        /// <summary>
        /// Sends the response immediately.
        /// A response can only be sent once.
        /// </summary>
        /// <returns></returns>
        public async ValueTask SendAsync() {
            if (_sent) {
                return;
            }
            _sent = true;

            // reserve (depending on body kind): body length or a ReadOnlyMemory 
            ReadOnlyMemory<byte> bodyMem = ReadOnlyMemory<byte>.Empty;
            long bodyLength = 0;

            if (_bodyKind == BodyKind.File) {
                AddHeader("Accept-Ranges", "bytes");
                RangeDecision range = EvaluateRange();
                switch (range.Kind) {

                    case RangeDecisionKind.Ok:
                        _fileRangeEnabled = true;
                        _fileRangeStart = range.Start;
                        _fileRangeLength = range.Length;

                        _statusCode = 206;
                        _statusText = DefaultStatusText(206);

                        AddHeader("Content-Range", $"bytes {range.Start}-{(range.Start + range.Length - 1)}/{_fileLength}");
                        break;

                    case RangeDecisionKind.BadRequest:
                        BuildRangeError(400, "Invalid Range header");
                        break;

                    case RangeDecisionKind.NotSatisfiable:
                        BuildRangeError(416, "Requested range not satisfiable", _fileLength);
                        break;

                    case RangeDecisionKind.None:
                    default:
                        // full file
                        break;
                }
            }

            switch (_bodyKind) {
                case BodyKind.None:
                    bodyMem = ReadOnlyMemory<byte>.Empty;
                    bodyLength = 0;
                    break;

                case BodyKind.Segment:
                case BodyKind.OwnedBuffer:
                    if (_bodyArray != null && _bodyLength > 0) {
                        bodyMem = _bodyArray.AsMemory(_bodyOffset, _bodyLength);
                        bodyLength = _bodyLength;
                    }
                    break;

                case BodyKind.OwnedWriter:
                    if (_ownedBodyWriter != null && _ownedBodyWriter.Length > 0) {
                        bodyMem = _ownedBodyWriter.Buffer.AsMemory(0, _ownedBodyWriter.Length);
                        bodyLength = _ownedBodyWriter.Length;
                    }
                    break;

                case BodyKind.Memory:
                    bodyMem = _bodyMemory;
                    bodyLength = bodyMem.Length;
                    break;

                case BodyKind.File:
                    bodyLength = _fileRangeEnabled ? _fileRangeLength : _fileLength;
                    // bodyMem remains empty, we stream after
                    break;
            }

            // negotiate/compress (optional)
            PooledBufferWriter? compressedWriter = null;
            ArraySegment<byte> compressedSeg = default;
            NegotiatedEncoding negotiated = NegotiatedEncoding.None;

            bool isMethodHead = _session.Request.Method == "HEAD";
            bool statusForbidsBody = (_statusCode >= 100 && _statusCode < 200)
                                     || _statusCode == 204
                                     || _statusCode == 205
                                     || _statusCode == 304;
            _suppressContentLength = _suppressContentLength
                                     || (_statusCode >= 100 && _statusCode < 200)
                                     || _statusCode == 204;

            bool canCompressBody = bodyLength > 0
                                   && _bodyKind != BodyKind.File
                                   && !_customContentLength.HasValue
                                   && !statusForbidsBody
                                   && !HasHeaderIgnoreCase("Content-Encoding")
                                   && IsCompressibleContentType(_contentType);

            if (_compressionMode != ResponseCompressionMode.Disabled && canCompressBody) {
                bool wantCompress = _compressionMode switch {
                    ResponseCompressionMode.Auto => (bodyLength >= _compressionMinSize),
                    ResponseCompressionMode.ForceGzip => true,
                    ResponseCompressionMode.ForceDeflate => true,
                    ResponseCompressionMode.ForceBrotli => true,
                    _ => false
                };

                if (wantCompress) {
                    bool allowGzip = _compressionMode != ResponseCompressionMode.ForceDeflate && _compressionMode != ResponseCompressionMode.ForceBrotli;
                    bool allowDeflate = _compressionMode != ResponseCompressionMode.ForceGzip && _compressionMode != ResponseCompressionMode.ForceBrotli;
                    bool allowBrotli = _compressionMode != ResponseCompressionMode.ForceGzip && _compressionMode != ResponseCompressionMode.ForceDeflate;
                    negotiated = NegotiateEncoding(_session.Request.Headers.AcceptEncoding, allowGzip, allowDeflate, allowBrotli);

                    if (negotiated != NegotiatedEncoding.None) {
                        compressedWriter = CompressToPooledWriter(_bufferPool, bodyMem, negotiated, _compressionLevel);
                        compressedSeg = new ArraySegment<byte>(compressedWriter.Buffer, 0, compressedWriter.Length);

                        bool forced = _compressionMode == ResponseCompressionMode.ForceGzip 
                                      || _compressionMode == ResponseCompressionMode.ForceDeflate
                                      || _compressionMode == ResponseCompressionMode.ForceBrotli;
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
                long finalBodyLength = (negotiated != NegotiatedEncoding.None && compressedWriter != null) ? compressedWriter.Length : bodyLength;
                if (_customContentLength.HasValue && _customContentLength.Value != finalBodyLength) {
                    throw new InvalidOperationException($"Custom Header Content-Length ({_customContentLength.Value}) does not match actual body length ({finalBodyLength}).");
                }

                if (!_suppressContentLength) {
                    WriteBytes(headerWriter, H_CL);
                    WriteLongAscii(headerWriter, statusForbidsBody && _statusCode != 304 ? 0 : (_customContentLength ?? finalBodyLength));
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
                    WriteBytes(headerWriter, negotiated switch {
                        NegotiatedEncoding.Gzip => H_GZIP,
                        NegotiatedEncoding.Deflate => H_DEFLATE,
                        NegotiatedEncoding.Brotli => H_BR,
                        _ => throw new ArgumentOutOfRangeException()
                    });
                    WriteCRLF(headerWriter);

                    // Vary: Accept-Encoding (avoid duplicates if caller already set it)
                    if (!HasHeaderValueIgnoreCase("Vary", "Accept-Encoding")) {
                        WriteBytes(headerWriter, H_VARY);
                        WriteAscii(headerWriter, "Accept-Encoding");
                        WriteCRLF(headerWriter);
                    }
                }

                // Date
                WriteBytes(headerWriter, GetCachedDateHeader());

                // Connection (session decides keep-alive/close)
                WriteBytes(headerWriter, H_CONN);
                if (_connection == null) {
                    WriteBytes(headerWriter, _session.CloseAfterResponse ? H_CONN_CLOSE : H_CONN_KA);
                }
                else {
                    WriteAscii(headerWriter, _connection);
                }
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
                if (isMethodHead || statusForbidsBody) {
                    await _session.SendAsync(headerSeg).ConfigureAwait(false);
                }
                else if (_bodyKind == BodyKind.File) {
                    // header
                    await _session.SendAsync(headerSeg).ConfigureAwait(false);
                    // stream body file
                    const int FileChunkSize = 64 * 1024;
                    using FileStream fs = new(
                        _filePath!,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: FileChunkSize,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan
                    );
                    long remaining = _fileRangeEnabled ? _fileRangeLength : _fileLength;
                    if (_fileRangeEnabled && _fileRangeStart > 0) {
                        fs.Seek(_fileRangeStart, SeekOrigin.Begin);
                    }
                    byte[] buf = _bufferPool.Rent(FileChunkSize);
                    try {
                        while (remaining > 0) {
                            int toRead = remaining > buf.Length ? buf.Length : (int)remaining;
                            int read = await fs.ReadAsync(buf.AsMemory(0, toRead)).ConfigureAwait(false);
                            if (read <= 0) {
                                break;
                            }
                            await _session.SendAsync(new ArraySegment<byte>(buf, 0, read)).ConfigureAwait(false);
                            remaining -= read;
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
                _session.NotifyResponseSent();
            }
            finally {
                compressedWriter?.Dispose();
                headerWriter?.Dispose();
                DisposeBody(); // disposes owner if any + returns owned buffers
            }
        }

        #endregion manipulate (high level methods)

        #region alias common response

        /// <summary>
        /// Sets a 404 Not Found response.
        /// </summary>
        /// <param name="body">The optional response body.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse NotFound(string? body = null) {
            DisposeBody();
            Status(404);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Sets a 500 Internal Server Error response.
        /// </summary>
        /// <param name="body">The optional response body.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse InternalServerError(string? body = null) {
            DisposeBody();
            Status(500);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Sets a 302 redirect response.
        /// </summary>
        /// <param name="url">The redirect target URL.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Redirect(string url) {
            DisposeBody();
            Status(302);
            AddHeader("Location", url);
            return this;
        }

        /// <summary>
        /// Sets a 401 Unauthorized response.
        /// </summary>
        /// <param name="body">The optional response body.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Unauthorized(string? body = null) {
            DisposeBody();
            Status(401);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Sets a 403 Forbidden response.
        /// </summary>
        /// <param name="body">The optional response body.</param>
        /// <returns>The current response instance.</returns>
        public HttpResponse Forbidden(string? body = null) {
            DisposeBody();
            Status(403);
            if (!string.IsNullOrWhiteSpace(body)) {
                Text(body);
            }
            return this;
        }

        /// <summary>
        /// Returns 401 if the user is unauthenticated; otherwise returns 403.
        /// </summary>
        /// <returns>The current response instance.</returns>
        public HttpResponse Access() {
            if (!_session.Principal.IsAuthenticated) {
                return Unauthorized();
            }
            return Forbidden();
        }

        /// <summary>
        /// Marks the response as a downloadable attachment.
        /// </summary>
        /// <returns>The current response instance.</returns>
        public HttpResponse Attachment(string outputFilename) {
            AddHeader("Content-Disposition", $"attachment;filename={outputFilename}");
            ContextTypeFromExtension(outputFilename);
            return this;
        }

        #endregion alias common response

        #region cookies

        /// <summary>
        /// Stores the cookies to write to the response.
        /// </summary>
        private CookieEntry[]? _cookies;

        /// <summary>
        /// The number of cookies currently queued.
        /// </summary>
        private int _cookieCount;

        /// <summary>
        /// Adds a Set-Cookie header to the response.
        /// </summary>
        /// <param name="name">The cookie name.</param>
        /// <param name="value">The cookie value.</param>
        /// <param name="options">The cookie options.</param>
        /// <returns>The current response instance.</returns>
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
            if (_cookies == null) {
                _cookies = new CookieEntry[2];
            }
            else if (_cookieCount == _cookies.Length) {
                Array.Resize(ref _cookies, _cookies.Length * 2);
            }
            _cookies[_cookieCount++] = new CookieEntry(name, value, options);
            return this;
        }

        /// <summary>
        /// Adds a Set-Cookie header that instructs the client to delete the cookie.
        /// </summary>
        /// <param name="name">The cookie name.</param>
        /// <param name="path">The cookie path.</param>
        /// <param name="domain">The cookie domain.</param>
        /// <returns>The current response instance.</returns>
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
        /// Removes all queued cookies from the response.
        /// </summary>
        /// <returns>The current response instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HttpResponse ClearCookies() {
            _cookieCount = 0;
            return this;
        }

        #region cookie structure

        /// <summary>
        /// The SameSite policy applied to a cookie.
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
        /// Defines the options used to write a cookie.
        /// </summary>
        public readonly struct CookieOptions {

            /// <summary>
            /// Path scope of the cookie.
            /// 
            /// Defines the URL path that must exist in the request for the browser to send the cookie.
            /// Example: "/api" means the cookie is only sent for requests starting with "/api".
            /// 
            /// Priority:
            /// - More specific paths take precedence over less specific ones when multiple cookies share the same name.
            /// - Does NOT override Domain rules, both must match.
            /// 
            /// Security note:
            /// - Restricting Path reduces exposure surface (good practice).
            /// </summary>
            public readonly string? Path;

            /// <summary>
            /// Domain scope of the cookie.
            /// 
            /// Defines which hosts can receive the cookie.
            /// Example:
            /// - "example.com" → sent to example.com AND all subdomains (api.example.com, etc.)
            /// - null → defaults to the current host ONLY (more restrictive, safer)
            /// 
            /// Priority:
            /// - Domain + Path must BOTH match for the cookie to be sent.
            /// - If multiple cookies share the same name, the most specific domain wins.
            /// 
            /// Security note:
            /// - Avoid setting a wide domain unless needed (limits attack surface).
            /// </summary>
            public readonly string? Domain;

            /// <summary>
            /// Max-Age in seconds.
            /// 
            /// Defines how long (relative to now) the cookie remains valid.
            /// 
            /// Priority:
            /// - Takes precedence over Expires if both are set (RFC 6265).
            /// - Preferred over Expires because it avoids client/server clock drift issues.
            /// </summary>
            public readonly int MaxAge;
            /// <summary>
            /// Indicates whether Max-Age is explicitly set.
            /// 
            /// Useful because:
            /// - 0 is a valid value (means "delete immediately")
            /// - So you need a flag to distinguish "not set" vs "set to 0"
            /// </summary>
            public readonly bool HasMaxAge;

            /// <summary>
            /// Absolute expiration date of the cookie.
            /// 
            /// Defines the exact date/time at which the cookie expires.
            /// 
            /// Priority:
            /// - Used ONLY if Max-Age is NOT set.
            /// - Ignored if Max-Age is present.
            /// 
            /// Caveat:
            /// - Depends on client clock → can be unreliable.
            /// </summary>
            public readonly DateTimeOffset Expires;
            /// <summary>
            /// Indicates whether Expires is explicitly set.
            /// 
            /// Same idea as HasMaxAge:
            /// - Allows distinguishing "not set" from "default value"
            /// </summary>
            public readonly bool HasExpires;

            /// <summary>
            /// Secure flag.
            /// 
            /// If true:
            /// - Cookie is ONLY sent over HTTPS connections.
            /// 
            /// Priority:
            /// - Independent from other attributes.
            /// - Strongly recommended in production.
            /// 
            /// Security:
            /// - Prevents leakage over HTTP (MITM).
            /// - REQUIRED if SameSite=None (modern browsers enforce this).
            /// </summary>
            public readonly bool Secure;

            /// <summary>
            /// HttpOnly flag.
            /// 
            /// If true:
            /// - Cookie is NOT accessible via JavaScript (document.cookie).
            /// - Only sent automatically by the browser in HTTP requests.
            /// 
            /// Priority:
            /// - Independent from SameSite / Secure.
            /// 
            /// Security:
            /// - Protects against XSS stealing the cookie.
            /// - DOES NOT prevent requests being made with the cookie (important nuance).
            /// </summary>
            public readonly bool HttpOnly;

            /// <summary>
            /// SameSite policy.
            /// 
            /// Controls whether the cookie is sent with cross-site requests.
            /// Values:
            /// - Strict: sent ONLY in same-site context (maximum protection)
            /// - Lax: sent on top-level navigation (GET links, etc.) but not on CSRF-prone requests
            /// - None: sent in ALL contexts (requires Secure=true)
            /// 
            /// Priority:
            /// - Enforced by the browser BEFORE sending the cookie.
            /// - Can block cookie even if Domain/Path match.
            /// 
            /// Security:
            /// - Primary defense against CSRF.
            /// 
            /// Gotchas:
            /// - SameSite=None REQUIRES Secure=true (otherwise cookie is rejected).
            /// - Cross-domain SPA/API setups often REQUIRE SameSite=None.
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
        /// Represents a cookie queued for the response.
        /// </summary>
        private readonly struct CookieEntry {
            public readonly string Name;
            public readonly string Value;
            public readonly CookieOptions Options;

            /// <summary>
            /// Initializes a cookie entry.
            /// </summary>
            /// <param name="name">The cookie name.</param>
            /// <param name="value">The cookie value.</param>
            /// <param name="options">The cookie options.</param>
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
        /// Resets the response so it can be reused for the next request.
        /// Already allocated arrays are kept.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() {
            _statusCode = 200;
            _statusText = "OK";

            _contentType = null;

            _headerCount = 0;
            _customContentLength = null;
            _suppressContentLength = false;

            _connection = null;

            _compressionMode = ResponseCompressionMode.Auto;
            _compressionMinSize = 512;
            _compressionLevel = CompressionLevel.Fastest;

            DisposeBody();

            _cookieCount = 0;

            _sent = false;
            BytesSent = 0;
        }

        /// <summary>
        /// Releases the current body and any owned resources.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DisposeBody() {
            // dispose explicit owner
            if (_bodyOwner != null) {
                _bodyOwner.Dispose();
                _bodyOwner = null;
            }

            // dispose writer (returns its pooled array)
            if (_ownedBodyWriter != null) {
                _ownedBodyWriter.Dispose();
                _ownedBodyWriter = null;
            }

            // return pooled buffer only if we own it
            if (_bodyKind == BodyKind.OwnedBuffer && _bodyArray != null) {
                _bufferPool.Return(_bodyArray);
            }

            _bodyKind = BodyKind.None;
            _bodyMemory = ReadOnlyMemory<byte>.Empty;
            _bodyArray = null;
            _bodyOffset = 0;
            _bodyLength = 0;

            _filePath = null;
            _fileLength = 0;
            _fileRangeEnabled = false;
            _fileRangeStart = 0;
            _fileRangeLength = 0;
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
        /// "br"
        /// </summary>
        private static ReadOnlySpan<byte> H_BR => "br"u8;

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
        /// Identifies how the response body is currently stored.
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
        /// Controls response compression behavior.
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
            ForceDeflate = 3,
            /// <summary>
            /// Force Brotli (if client allows it)
            /// </summary>
            ForceBrotli = 4
        }

        /// <summary>
        /// The content encoding selected for the response.
        /// </summary>
        private enum NegotiatedEncoding : byte { None = 0, Gzip = 1, Deflate = 2, Brotli = 3 }

        /// <summary>
        /// Returns true if the response contains a header with the specified name.
        /// </summary>
        /// <param name="name">The header name to search for.</param>
        /// <returns>True if the header exists; otherwise false.</returns>
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
        /// Returns true if the response contains the specified header name and value.
        /// </summary>
        /// <param name="headerName">The header name to search for.</param>
        /// <param name="headerValue">The header value to search for.</param>
        /// <returns>True if a matching header is found; otherwise false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasHeaderValueIgnoreCase(string headerName, string headerValue) {
            for (int i = 0; i < _headerCount; i++) {
#if NET9_0_OR_GREATER
                ref readonly HeaderEntry h = ref _headers[i];
#else
                HeaderEntry h = _headers[i];
#endif
                if (!string.IsNullOrEmpty(h.Name) && h.Name.Equals(headerName, StringComparison.OrdinalIgnoreCase)) {
                    string v = h.Value ?? string.Empty;
                    if (v.IndexOf(headerValue, StringComparison.OrdinalIgnoreCase) >= 0) {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether the content type is suitable for compression.
        /// </summary>
        /// <param name="contentType">The content type to evaluate.</param>
        /// <returns>True if compression is allowed; otherwise false.</returns>
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
        /// Selects the best encoding based on the client's Accept-Encoding header and server settings.
        /// </summary>
        /// <param name="acceptEncoding">The raw Accept-Encoding header.</param>
        /// <param name="allowGzip">Whether gzip is allowed.</param>
        /// <param name="allowDeflate">Whether deflate is allowed.</param>
        /// <param name="allowBrotli">Whether Brotli is allowed.</param>
        /// <returns>The selected encoding.</returns>
        private static NegotiatedEncoding NegotiateEncoding(string? acceptEncoding, bool allowGzip, bool allowDeflate, bool allowBrotli) {
            if (string.IsNullOrEmpty(acceptEncoding)) {
                return NegotiatedEncoding.None;
            }

            float qGzip = -1f;
            float qDeflate = -1f;
            float qBrotli = -1f;

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
                else if (name.Equals("br".AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                    qBrotli = q;
                }
            }

            if (!allowGzip) {
                qGzip = -1f;
            }
            if (!allowDeflate) {
                qDeflate = -1f;
            }
            if (!allowBrotli) {
                qBrotli = -1f;
            }

            // pick best q (strictly > 0)
            float best = 0f;
            NegotiatedEncoding bestEnc = NegotiatedEncoding.None;

            if (qGzip > best) { best = qGzip; bestEnc = NegotiatedEncoding.Gzip; }
            if (qDeflate > best) { best = qDeflate; bestEnc = NegotiatedEncoding.Deflate; }
            if (qBrotli > best) { best = qBrotli; bestEnc = NegotiatedEncoding.Brotli; }

            return best > 0f ? bestEnc : NegotiatedEncoding.None;
        }

        /// <summary>
        /// Compresses the input into a pooled buffer writer.
        /// </summary>
        /// <param name="pool">The array pool to use.</param>
        /// <param name="input">The input payload.</param>
        /// <param name="encoding">The encoding to apply.</param>
        /// <param name="level">The compression level.</param>
        /// <returns>A pooled writer containing the compressed payload.</returns>
        private static PooledBufferWriter CompressToPooledWriter(ArrayPool<byte> pool, ReadOnlyMemory<byte> input, NegotiatedEncoding encoding, CompressionLevel level) {
            int init = input.Length <= 0 ? 256 : Math.Min(Math.Max(256, input.Length / 2), 64 * 1024);
            PooledBufferWriter output = new(pool, initialSize: init);

            try {
                using Stream bw = new BufferWriterStream(output);
                Stream compressor = encoding switch {
                    NegotiatedEncoding.Gzip => new GZipStream(bw, level, leaveOpen: true),
                    NegotiatedEncoding.Deflate => new DeflateStream(bw, level, leaveOpen: true),
                    NegotiatedEncoding.Brotli => new BrotliStream(bw, level, leaveOpen: true),
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

        #region range helpers

        /// <summary>
        /// Evaluates the request Range header for the current file response.
        /// </summary>
        /// <returns>The evaluated range decision.</returns>
        private RangeDecision EvaluateRange() {

            if (_bodyKind != BodyKind.File) {
                return new(RangeDecisionKind.None);
            }
            if (_statusCode != 200) {
                return new(RangeDecisionKind.None);
            }

            if (_session.Request.Method != "GET" && _session.Request.Method != "HEAD") {
                return new(RangeDecisionKind.None);
            }

            if (!_session.Request.Headers.TryGetValue("Range", out string? rawRange) || string.IsNullOrWhiteSpace(rawRange)) {
                return new(RangeDecisionKind.None);
            }

            // try parse range
            rawRange = rawRange.Trim();

            if (_fileLength <= 0) {
                return new(RangeDecisionKind.NotSatisfiable);
            }
            if (!rawRange.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) {
                return new(RangeDecisionKind.BadRequest);
            }

            string spec = rawRange.Substring("bytes=".Length).Trim();

            // unsupported multi-range
            if (spec.AsSpan().IndexOf(',') >= 0) {
                return new(RangeDecisionKind.BadRequest);
            }

            int dash = spec.IndexOf('-');
            if (dash < 0) {
                return new(RangeDecisionKind.BadRequest);
            }

            string startPart = spec.Substring(0, dash).Trim();
            string endPart = spec.Substring(dash + 1).Trim();

            // suffix: bytes=-500
            if (startPart.Length == 0) {
                if (!long.TryParse(endPart, out long suffixLength) || suffixLength <= 0) {
                    return new(RangeDecisionKind.BadRequest);
                }

                if (suffixLength >= _fileLength) {
                    return new(RangeDecisionKind.Ok, 0, _fileLength);
                }

                return new(RangeDecisionKind.Ok, _fileLength - suffixLength, suffixLength);
            }

            if (!long.TryParse(startPart, out long parsedStart) || parsedStart < 0) {
                return new(RangeDecisionKind.BadRequest);
            }

            // bytes=500-
            if (endPart.Length == 0) {
                if (parsedStart >= _fileLength) {
                    return new(RangeDecisionKind.NotSatisfiable);
                }
                return new(RangeDecisionKind.Ok, parsedStart, _fileLength - parsedStart);
            }

            if (!long.TryParse(endPart, out long parsedEnd) || parsedEnd < parsedStart) {
                return new(RangeDecisionKind.BadRequest);
            }

            if (parsedStart >= _fileLength) {
                return new(RangeDecisionKind.NotSatisfiable);
            }

            if (parsedEnd >= _fileLength) {
                parsedEnd = _fileLength - 1;
            }
            return new(RangeDecisionKind.Ok, parsedStart, (parsedEnd - parsedStart) + 1);
        }

        /// <summary>
        /// Builds an error response for an invalid or unsatisfied range request.
        /// </summary>
        /// <param name="status">The HTTP status code to use.</param>
        /// <param name="message">The error message.</param>
        /// <param name="fullLength">The full file length, when relevant.</param>
        private void BuildRangeError(int status, string message, long? fullLength = null) {

            DisposeBody();

            _statusCode = status;
            _statusText = message;
            _contentType = "text/plain; charset=utf-8";

            if (status == 416 && fullLength.HasValue) {
                AddHeader("Content-Range", $"bytes */{fullLength.Value}");
            }

            byte[] buf = _bufferPool.Rent(128);
            int len = Utf8NoBom.GetBytes(message.AsSpan(), buf.AsSpan());

            _bodyKind = BodyKind.OwnedBuffer;
            _bodyArray = buf;
            _bodyOffset = 0;
            _bodyLength = len;

            _filePath = null;
            _fileLength = 0;
            _fileRangeEnabled = false;
            _fileRangeStart = 0;
            _fileRangeLength = 0;
        }

        /// <summary>
        /// RangeDecision
        /// </summary>
        private readonly struct RangeDecision {
            public readonly RangeDecisionKind Kind;
            public readonly long Start;
            public readonly long Length;

            public RangeDecision(RangeDecisionKind kind, long start = 0, long length = 0) {
                Kind = kind;
                Start = start;
                Length = length;
            }
        }

        /// <summary>
        /// The possible outcomes of range evaluation.
        /// </summary>
        private enum RangeDecisionKind : byte {
            None = 0,
            Ok = 1,
            BadRequest = 2,
            NotSatisfiable = 3
        }

        #endregion range helpers

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
        /// Writes a date using RFC1123 format.
        /// </summary>
        /// <param name="w">The target writer.</param>
        /// <param name="dto">The date to write.</param>
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
        /// Writes all queued Set-Cookie headers.
        /// </summary>
        /// <param name="headerWriter">The target header writer.</param>
        private void WriteCookie(PooledBufferWriter headerWriter) {
            if (_cookieCount == 0) {
                return;
            }
            CookieEntry[]? cookies = _cookies;
            if (cookies == null) {
                return;
            }

            for (int i = 0; i < _cookieCount; i++) {
                ref readonly CookieEntry c = ref cookies[i];
                if (string.IsNullOrEmpty(c.Name)) {
                    continue;
                }

                WriteAscii(headerWriter, SET_COOKIE);

                // name=value
                WriteAscii(headerWriter, c.Name);
                WriteBytes(headerWriter, EQ);
                WriteAscii(headerWriter, c.Value ?? string.Empty);

                // attributes
                ref readonly CookieOptions o = ref Unsafe.AsRef(in c.Options);

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

        #region cache date header

        private static long _dateHeaderUnixSeconds = -1;
        private static byte[]? _dateHeaderBytes;

        /// <summary>
        /// Returns a cached Date header for the current second.
        /// </summary>
        /// <returns>The cached Date header bytes.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlySpan<byte> GetCachedDateHeader() {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            byte[]? cached = Volatile.Read(ref _dateHeaderBytes);

            if (cached != null && Volatile.Read(ref _dateHeaderUnixSeconds) == now) {
                return cached;
            }

            string s = "Date: " + DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime.ToString("r", System.Globalization.CultureInfo.InvariantCulture) + "\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(s);

            Volatile.Write(ref _dateHeaderBytes, bytes);
            Volatile.Write(ref _dateHeaderUnixSeconds, now);

            return bytes;
        }

        #endregion cache date header

    }

}
