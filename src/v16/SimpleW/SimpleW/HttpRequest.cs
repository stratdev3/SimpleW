using System;


namespace NetCoreServer {

    /// <summary>
    /// HTTP request is used to create or process parameters of HTTP protocol request(method, URL, headers, etc).
    /// </summary>
    /// <remarks>Not thread-safe.</remarks>
    public partial class HttpRequest {

        /// <summary>
        /// Clear Extend datas
        /// </summary>
        private void ClearExtend() {
            TrustXHeaders = false;
            _uri = null;
            _fullQualifiedUrl = null;

            _headerXRealIp = null;
            _headerUserAgent = null;
            _headerAcceptEncodings = null;
            _headerAuthorization = null;
            _headerContentType = null;
        }

        #region extend_url_properties

        /// <summary>
        /// True to allow some headers as source of truth for Telemetry
        /// Example : X-Forwarded-Host, X-Real-IP (...) are often used to pass data
        ///           from reverse proxy (nginx...) to upstream server.
        /// Note : you should allow only if you have a reverse proxy with well defined settings policy
        /// </summary>
        public bool TrustXHeaders { get; set; } = false;


        /// <summary>
        /// The Uri sanitized and extract from Url
        /// </summary>
        private Uri _uri;

        /// <summary>
        /// The Uri sanitized and extract from Url
        /// </summary>
        public Uri Uri {
            get {
                if (_uri == null) {
                    if (FullQualifiedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
                        try {
                            _uri = new Uri(FullQualifiedUrl);
                        }
                        catch { }
                    }
                }
                return _uri;
            }
        }


        /// <summary>
        /// The Full Qualified Url
        /// </summary>
        private string _fullQualifiedUrl;

        /// <summary>
        /// The Full Qualified Url
        /// </summary>
        public string FullQualifiedUrl {
            get {
                if (_fullQualifiedUrl == null) {
                    string host = null;
                    string scheme = null;
                    string xhost = null;

                    // loop on all intesting headers once
                    for (int i = 0; i < _headers.Count; i++) {
                        (string headerName, string headerValue) = _headers[i];
                        if (host == null && string.Equals(headerName, "Host", StringComparison.OrdinalIgnoreCase)) {
                            host = headerValue;
                        }
                        else if (_headerUserAgent == null && string.Equals(headerName, "User-Agent", StringComparison.OrdinalIgnoreCase)) {
                            _headerUserAgent = headerValue;
                        }
                        else if (_headerAcceptEncodings == null && string.Equals(headerName, "Accept-Encoding", StringComparison.OrdinalIgnoreCase)) {
                            _headerAcceptEncodings = headerValue;
                        }
                        else if (_headerAuthorization == null && string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase)) {
                            _headerAuthorization = headerValue;
                        }
                        else if (_headerContentType == null && string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase)) {
                            _headerContentType = headerValue;
                        }
                        else if (TrustXHeaders) {
                            if (scheme == null && string.Equals(headerName, "X-Forwarded-Proto", StringComparison.OrdinalIgnoreCase)) {
                                scheme = headerValue;
                            }
                            else if (xhost == null && string.Equals(headerName, "X-Forwarded-Host", StringComparison.OrdinalIgnoreCase)) {
                                xhost = headerValue;
                            }
                            else if (_headerXRealIp == null && string.Equals(headerName, "X-Real-IP", StringComparison.OrdinalIgnoreCase)) {
                                _headerXRealIp = headerValue;
                            }
                        }
                        if (host != null && xhost != null && scheme != null 
                            && _headerXRealIp != null 
                            && _headerUserAgent != null 
                            && _headerAcceptEncodings != null
                            && _headerAuthorization != null
                            && _headerContentType != null
                        ) {
                            break;
                        }
                    }
                    _fullQualifiedUrl = Uri.UnescapeDataString((scheme ?? "http") + "://" + (xhost ?? host) + Url);
                }
                return _fullQualifiedUrl;
            }
        }


        /// <summary>
        /// Return true if FullQualifiedUrl ends with "/"
        /// </summary>
        public bool hasEndingSlash => !string.IsNullOrEmpty(FullQualifiedUrl) && FullQualifiedUrl[^1] == '/';

        #endregion extend_url_properties

        #region header_cached

        /// <summary>
        /// X-RealIp header
        /// </summary>
        private string _headerXRealIp;

        /// <summary>
        /// X-RealIp header
        /// </summary>
        public string HeaderXRealIp => _headerXRealIp;


        /// <summary>
        /// User-Agent header
        /// </summary>
        private string _headerUserAgent;

        /// <summary>
        /// User-Agent header
        /// </summary>
        public string HeaderUserAgent => _headerUserAgent;


        /// <summary>
        /// Accept-Encodings header
        /// </summary>
        public string _headerAcceptEncodings;

        /// <summary>
        /// Accept-Encodings header
        /// </summary>
        public string HeaderAcceptEncodings => _headerAcceptEncodings;


        /// <summary>
        /// Authorization header
        /// </summary>
        private string _headerAuthorization;

        /// <summary>
        /// Authorization header
        /// </summary>
        public string HeaderAuthorization => _headerAuthorization;


        /// <summary>
        /// Content-Type header
        /// </summary>
        private string _headerContentType;

        /// <summary>
        /// Content-Type header
        /// </summary>
        public string HeaderContentType => _headerContentType;


        #endregion header_cached

    }

}
