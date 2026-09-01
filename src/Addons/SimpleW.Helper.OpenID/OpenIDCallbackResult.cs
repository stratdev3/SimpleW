namespace SimpleW.Helper.OpenID {

    /// <summary>
    /// Result returned by CompleteCallbackAsync.
    /// </summary>
    public sealed class OpenIDCallbackResult {

        #region properties

        /// <summary>
        /// True when authentication completed.
        /// </summary>
        public bool IsSuccess { get; init; }

        /// <summary>
        /// HTTP status code suitable for an error response.
        /// </summary>
        public int StatusCode { get; init; }

        /// <summary>
        /// Error message when IsSuccess is false.
        /// </summary>
        public string? Error { get; init; }

        /// <summary>
        /// Provider logical name.
        /// </summary>
        public string Provider { get; init; } = string.Empty;

        /// <summary>
        /// Application return URL.
        /// </summary>
        public string ReturnUrl { get; init; } = "/";

        /// <summary>
        /// Authenticated principal.
        /// </summary>
        public HttpPrincipal? Principal { get; init; }

        #endregion properties

        #region factories

        internal static OpenIDCallbackResult Success(
            string provider,
            HttpPrincipal principal,
            string returnUrl
        ) {
            return new OpenIDCallbackResult {
                IsSuccess = true,
                StatusCode = 200,
                Provider = provider,
                Principal = principal,
                ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
            };
        }

        internal static OpenIDCallbackResult Fail(string provider, int statusCode, string? error, string? returnUrl = null) {
            return new OpenIDCallbackResult {
                IsSuccess = false,
                StatusCode = statusCode,
                Provider = provider,
                Error = string.IsNullOrWhiteSpace(error) ? "openid_error" : error,
                ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
            };
        }

        #endregion factories

    }

}
