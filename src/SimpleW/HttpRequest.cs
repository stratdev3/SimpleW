using System.Buffers;
using System.Text;
using SimpleW.Parsers;
using SimpleW.Security;


namespace SimpleW {

    /// <summary>
    /// HTTP request is used to create or process parameters of HTTP protocol request (method, URL, headers, etc).
    /// </summary>
    public sealed class HttpRequest {

        /// <summary>
        /// Get the HTTP request method
        /// </summary>
        public string Method { get; private set; } = string.Empty;

        /// <summary>
        /// Get the HTTP request Path
        /// </summary>
        public string Path { get; private set; } = string.Empty;

        /// <summary>
        /// Raw target (path + query)
        /// </summary>
        public string RawTarget { get; private set; } = string.Empty;

        /// <summary>
        /// HTTP protocol version (e.g. "HTTP/1.1")
        /// </summary>
        public string Protocol { get; private set; } = string.Empty;

        /// <summary>
        /// HTTP headers (case-insensitive)
        /// </summary>
        public HttpHeaders Headers { get; private set; }

        /// <summary>
        /// Body (buffer only valid during the time of the underlying Handler)
        /// </summary>
        public ReadOnlySequence<byte> Body { get; private set; } = ReadOnlySequence<byte>.Empty;

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
        /// QueryString as String (e.g: key1=value1&amp;key2=value2)
        /// </summary>
        public string QueryString { get; private set; } = string.Empty;

        /// <summary>
        /// Flag to Query parsing
        /// </summary>
        private bool _queryInitialized = false;

        /// <summary>
        /// QueryString Dictionnary parsing
        /// </summary>
        private Dictionary<string, string>? _query;

        /// <summary>
        /// QueryString as Dictionnary
        /// </summary>
        public Dictionary<string, string> Query {
            get {
                if (!_queryInitialized) {
                    _queryInitialized = true;
                    _query ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(QueryString)) {
                        HttpRequestParser.ParseQueryString(QueryString.AsSpan(), _query);
                    }
                }
                return _query!;
            }
        }

        /// <summary>
        /// Route values extracted from matched route (ex: :id, :path*)
        /// Null when route has no parameters.
        /// </summary>
        public Dictionary<string, string>? RouteValues { get; private set; }

        /// <summary>
        /// JsonEngine
        /// Can be used to parse body
        /// </summary>
        public readonly IJsonEngine JsonEngine;

        /// <summary>
        /// Max size of request headers in bytes
        /// </summary>
        public readonly int MaxRequestHeaderSize;

        /// <summary>
        /// Max size of request body in bytes
        /// </summary>
        public readonly long MaxRequestBodySize;

        /// <summary>
        /// Matched route template for openTelemetry
        /// </summary>
        public string? RouteTemplate { get; private set; }

        #region jwt

        /// <summary>
        /// _jwt already initialized ?
        /// </summary>
        private bool _jwtInitialized;

        /// <summary>
        /// Store the jwt value
        /// </summary>
        private string? _jwt;

        /// <summary>
        /// Jwt (raw string)
        /// </summary>
        public string? Jwt {
            get {
                if (_jwtInitialized) {
                    return _jwt;
                }
                _jwtInitialized = true;
                _jwt = JwtResolver(this);
                return _jwt;
            }
        }

        /// <summary>
        /// JwtResolver
        /// </summary>
        private readonly JwtResolver JwtResolver;

        /// <summary>
        /// JwtOptions
        /// </summary>
        public readonly JwtOptions? JwtOptions;

        /// <summary>
        /// _jwtToken already initialized ?
        /// </summary>
        private bool _jwtTokenInitialized;

        /// <summary>
        /// Store the JwtToken value
        /// </summary>
        private JwtToken? _jwtToken;

        /// <summary>
        /// JwtToken
        /// </summary>
        public JwtToken? JwtToken {
            get {
                if (!_jwtTokenInitialized) {
                    _jwtTokenInitialized = true;
                    if (string.IsNullOrWhiteSpace(Jwt)) {
                        JwtError = Security.JwtError.InvalidBase64;
                    }
                    else if (JwtOptions == null) {
                        JwtError = Security.JwtError.InvalidJsonOptions;
                    }
                    else if (!Security.Jwt.TryDecodeAndValidate(JsonEngine, Jwt, JwtOptions, out JwtToken? jwtToken, out JwtError err)) {
                        JwtError = err;
                    }
                    else {
                        _jwtToken = jwtToken;
                    }
                }
                return _jwtToken;
            } 
            set {
                _jwtTokenInitialized = true;
                _jwtToken = value;
                JwtError = Security.JwtError.None;
            }
        }

        /// <summary>
        /// JwtError
        /// </summary>
        public JwtError? JwtError { get; internal set; } = Security.JwtError.None;

        #endregion jwt

        #region webuser

        /// <summary>
        /// _webUser already initialized ?
        /// </summary>
        private bool _webUserInitialized;

        /// <summary>
        /// cache for webuser property
        /// </summary>
        private IWebUser _webUser = new WebUser();

        /// <summary>
        /// WebUserResolver
        /// </summary>
        private readonly WebUserResolver? _webUserResolver;

        /// <summary>
        /// WebUser (lazy) resolved from the current request.
        /// Can be overridden by :
        ///     - setting it (e.g: in a middleware)
        ///     OR
        ///     - override the SimpleWServer.ConfigureWebUserResolver()
        /// </summary>
        public IWebUser WebUser {
            get {
                if (!_webUserInitialized) {
                    _webUserInitialized = true;
                    if (_webUserResolver != null) {
                        _webUser = _webUserResolver(this);
                    }
                }
                return _webUser;
            }
            set {
                _webUserInitialized = true;
                _webUser = value;
            }
        }

        #endregion webuser

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="jsonEngine"></param>
        /// <param name="maxRequestHeaderSize"></param>
        /// <param name="maxRequestBodySize"></param>
        /// <param name="jwtResolver"></param>
        /// <param name="jwtOptions"></param>
        /// <param name="webUserResolver"></param>
        internal HttpRequest(IJsonEngine jsonEngine, int maxRequestHeaderSize, long maxRequestBodySize, JwtResolver jwtResolver, JwtOptions? jwtOptions, WebUserResolver? webUserResolver) {
            JsonEngine = jsonEngine;
            MaxRequestHeaderSize = maxRequestHeaderSize;
            MaxRequestBodySize = maxRequestBodySize;
            JwtResolver = jwtResolver;
            JwtOptions = jwtOptions;
            _webUserResolver = webUserResolver;
        }

        /// <summary>
        /// Reset HttpRequest for reuse
        /// </summary>
        public void Reset() {
            Method = string.Empty;
            Path = string.Empty;
            RawTarget = string.Empty;
            Protocol = string.Empty;
            QueryString = string.Empty;

            Headers = default;
            Body = ReadOnlySequence<byte>.Empty;

            _queryInitialized = false;
            _query?.Clear();

            RouteValues = null;
            RouteTemplate = null;

            _jwtInitialized = false;
            _jwt = null;

            _jwtTokenInitialized = false;
            _jwtToken = null;
            JwtError = Security.JwtError.None;

            _webUserInitialized = false;
            _webUser = new WebUser();
        }

        #region buffer

        /// <summary>
        /// public Property to received a buffer from an ArrayPool
        /// </summary>
        public byte[]? PooledBodyBuffer { get; set; }

        /// <summary>
        /// Return the buffer to ArrayPool
        /// </summary>
        public void ReturnPooledBodyBuffer() {
            if (PooledBodyBuffer is not null) {
                ArrayPool<byte>.Shared.Return(PooledBodyBuffer);
                PooledBodyBuffer = null;
            }
        }

        #endregion buffer

        #region helpers

        /// <summary>
        /// Alias to UTF8Encoding
        /// </summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Set Method
        /// </summary>
        /// <param name="method"></param>
        public void ParserSetMethod(string method) {
            Method = method;
        }
        /// <summary>
        /// Set Path
        /// </summary>
        /// <param name="path"></param>
        public void ParserSetPath(string path) {
            Path = path;
        }
        /// <summary>
        /// Set RawTarget
        /// </summary>
        /// <param name="rawTarget"></param>
        public void ParserSetRawTarget(string rawTarget) {
            RawTarget = rawTarget;
        }
        /// <summary>
        /// Set Protocol
        /// </summary>
        /// <param name="protocol"></param>
        public void ParserSetProtocol(string protocol) {
            Protocol = protocol;
        }
        /// <summary>
        /// Set Headers
        /// </summary>
        public void ParserSetHeaders(HttpHeaders headers) {
            Headers = headers;
        }
        /// <summary>
        /// Set Body
        /// </summary>
        /// <param name="body"></param>
        public void ParserSetBody(ReadOnlySequence<byte> body) {
            Body = body;
        }
        /// <summary>
        /// Set QueryString
        /// </summary>
        /// <param name="qs"></param>
        public void ParserSetQueryString(string qs) {
            QueryString = qs;
        }
        /// <summary>
        /// Set RouteValues
        /// </summary>
        /// <param name="rv"></param>
        public void ParserSetRouteValues(Dictionary<string, string>? rv) {
            RouteValues = rv;
        }
        /// <summary>
        /// Set RouteTemplate
        /// </summary>
        public void ParserSetRouteTemplate(string? template) {
            RouteTemplate = template;
        }

        #endregion helpers

    }

    /// <summary>
    /// JwtResolver
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public delegate string? JwtResolver(HttpRequest request);

    /// <summary>
    /// WebUserResolver
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public delegate IWebUser WebUserResolver(HttpRequest request);

    /// <summary>
    /// Examples of WebUserResolver
    /// </summary>
    public static class WebUserResolvers {

        /// <summary>
        /// TokenWebUser as IWebUser
        /// </summary>
        public static readonly WebUserResolver TokenWebUser = (request) => {
            if (request.JwtToken == null) {
                return new WebUser();
            }
            try {
                TokenWebUser twu = request.JsonEngine.Deserialize<TokenWebUser>(request.JwtToken.RawPayload);
                twu.Token = request.Jwt;
                return twu;
            }
            catch {
                return new WebUser();
            }
        };

    }

    /// <summary>
    /// HttpHeaders
    ///     1. most common
    ///     2. fallback list
    /// </summary>
    public struct HttpHeaders {

        /// <summary>
        /// Host
        /// </summary>
        public string? Host;

        /// <summary>
        /// Content-Type
        /// </summary>
        public string? ContentType;

        /// <summary>
        /// Content-Length
        /// raw string "123", parser use long
        /// </summary>
        public string? ContentLengthRaw;

        /// <summary>
        /// User Agent
        /// </summary>
        public string? UserAgent;

        /// <summary>
        /// Accept
        /// </summary>
        public string? Accept;

        /// <summary>
        /// Accept Encoding
        /// </summary>
        public string? AcceptEncoding;

        /// <summary>
        /// Accept Language
        /// </summary>
        public string? AcceptLanguage;

        /// <summary>
        /// Contection
        /// </summary>
        public string? Connection;

        /// <summary>
        /// Transfert Encoding
        /// </summary>
        public string? TransferEncoding;

        /// <summary>
        /// Cookie
        /// </summary>
        public string? Cookie;

        /// <summary>
        /// Upgrade
        /// </summary>
        public string? Upgrade;

        /// <summary>
        /// Authorization
        /// </summary>
        public string? Authorization;

        /// <summary>
        /// SecWebSocketKey
        /// </summary>
        public string? SecWebSocketKey;

        /// <summary>
        /// SecWebSocketVersion
        /// </summary>
        public string? SecWebSocketVersion;

        /// <summary>
        /// SecWebSocketProtocol
        /// </summary>
        public string? SecWebSocketProtocol;

        /// <summary>
        /// fallback for all other headers
        /// </summary>
        private HeaderEntry[]? _other;

        /// <summary>
        /// number of other headers
        /// </summary>
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
            if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) {
                Upgrade = value;
                return;
            }
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) {
                Authorization = value;
                return;
            }
            if (name.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase)) {
                SecWebSocketKey = value;
                return;
            }
            if (name.Equals("Sec-WebSocket-Version", StringComparison.OrdinalIgnoreCase)) {
                SecWebSocketVersion = value;
                return;
            }
            if (name.Equals("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase)) {
                SecWebSocketProtocol = value;
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
            if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) {
                value = Upgrade;
                return value is not null;
            }
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) {
                value = Authorization;
                return value is not null;
            }
            if (name.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase)) {
                value = SecWebSocketKey;
                return value is not null;
            }
            if (name.Equals("Sec-WebSocket-Version", StringComparison.OrdinalIgnoreCase)) {
                value = SecWebSocketVersion;
                return value is not null;
            }
            if (name.Equals("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase)) {
                value = SecWebSocketProtocol;
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
            if (Upgrade is not null) {
                yield return new("Upgrade", Upgrade);
            }
            if (Authorization is not null) {
                yield return new("Authorization", Authorization);
            }
            if (SecWebSocketKey is not null) {
                yield return new("Sec-WebSocket-Key", SecWebSocketKey);
            }
            if (SecWebSocketVersion is not null) {
                yield return new("Sec-WebSocket-Version", SecWebSocketVersion);
            }
            if (SecWebSocketProtocol is not null) {
                yield return new("Sec-WebSocket-Protocol", SecWebSocketProtocol);
            }

            // fallback
            if (_other is not null) {
                for (int i = 0; i < _otherCount; i++) {
#if NET9_0_OR_GREATER
                    ref readonly HeaderEntry e = ref _other[i];
#else
                    HeaderEntry e = _other[i];
#endif
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

        /// <summary>
        /// Name
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Value
        /// </summary>
        public readonly string Value;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        public HeaderEntry(string name, string value) {
            Name = name;
            Value = value;
        }
    }

}
