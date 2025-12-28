namespace SimpleW.Modules {

    /// <summary>
    /// CorsModuleExtension
    /// </summary>
    public static class CorsModuleExtension {

        /// <summary>
        /// Use CORS Module
        /// It setups a Middleware
        /// </summary>
        /// <example>
        /// server.UseCorsModule(o => {
        ///     o.Prefix = "/api";
        ///     o.AllowedOrigins = new[] { "http://localhost:5173", "https://app.example.com" };
        ///     o.AllowCredentials = true;
        ///     o.AllowedHeaders = "Content-Type, Authorization";
        ///     o.AllowedMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
        ///     o.MaxAgeSeconds = 600;
        /// });
        /// </example>
        public static SimpleWServer UseCorsModule(this SimpleWServer server, Action<CorsOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            CorsOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new CorsModule(options));
            return server;
        }

        /// <summary>
        /// CORS Options
        /// </summary>
        public sealed class CorsOptions {

            /// <summary>
            /// Apply CORS only for paths starting with this prefix (default "/")
            /// </summary>
            public string Prefix { get; set; } = "/";

            /// <summary>
            /// Allow all origins ("*").
            /// Note: if AllowCredentials is true, "*" cannot be used => origin is echoed instead.
            /// </summary>
            public bool AllowAnyOrigin { get; set; } = false;

            /// <summary>
            /// Allowed origins list (exact match, case-insensitive)
            /// </summary>
            public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

            /// <summary>
            /// Optional custom origin validator (return true to allow).
            /// If set, it has priority over AllowedOrigins.
            /// </summary>
            public Func<string, bool>? OriginValidator { get; set; }

            /// <summary>
            /// Allow cookies/Authorization header to be sent by browser.
            /// </summary>
            public bool AllowCredentials { get; set; } = false;

            /// <summary>
            /// Allowed methods for preflight response.
            /// If empty, the module will echo the Access-Control-Request-Method (when present),
            /// otherwise default to common methods.
            /// </summary>
            public string? AllowedMethods { get; set; } = "GET, POST, PUT, PATCH, DELETE, OPTIONS";

            /// <summary>
            /// Allowed headers for preflight response.
            /// If empty, the module will echo Access-Control-Request-Headers (when present).
            /// </summary>
            public string? AllowedHeaders { get; set; }

            /// <summary>
            /// Response headers accessible from JS (non-simple headers)
            /// </summary>
            public string? ExposedHeaders { get; set; }

            /// <summary>
            /// Preflight cache duration (Access-Control-Max-Age)
            /// </summary>
            public int? MaxAgeSeconds { get; set; }

            /// <summary>
            /// Validate and normalize
            /// </summary>
            public CorsOptions ValidateAndNormalize() {
                if (string.IsNullOrWhiteSpace(Prefix)) {
                    throw new ArgumentException($"{nameof(CorsOptions)}.{nameof(Prefix)} must not be null or empty.", nameof(Prefix));
                }

                Prefix = SimpleWExtension.NormalizePrefix(Prefix);

                // normalize origins (trim, remove empties)
                AllowedOrigins = (AllowedOrigins ?? Array.Empty<string>())
                                    .Select(o => (o ?? string.Empty).Trim())
                                    .Where(o => o.Length > 0)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToArray();

                // if user says allow any + credentials => we must echo origin (not "*")
                // we keep AllowAnyOrigin = true but middleware will choose echo behavior.
                return this;
            }
        }

        /// <summary>
        /// Cors Module
        /// </summary>
        private sealed class CorsModule : IHttpModule {

            private readonly CorsOptions _options;
            private readonly HashSet<string> _allowedOrigins;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="options"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public CorsModule(CorsOptions options) {
                _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
                _allowedOrigins = new HashSet<string>(_options.AllowedOrigins, StringComparer.OrdinalIgnoreCase);
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("CorsModule must be installed before server start.");
                }

                // Global middleware (but we filter by Prefix)
                server.UseMiddleware((session, next) => MiddlewareAsync(session, next));
            }

            /// <summary>
            /// Middleware
            /// </summary>
            /// <param name="session"></param>
            /// <param name="next"></param>
            /// <returns></returns>
            private ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next) {

                // Path filter
                if (!session.Request.Path.StartsWith(_options.Prefix, StringComparison.Ordinal)) {
                    return next();
                }

                // Browser sends Origin for CORS (and for same-origin fetch sometimes, but ok)
                if (!session.Request.Headers.TryGetValue("Origin", out var origin) || string.IsNullOrWhiteSpace(origin)) {
                    return next();
                }

                origin = origin.Trim();

                if (!IsOriginAllowed(origin)) {
                    // If origin not allowed: do NOT add CORS headers.
                    // Browser will block the response, which is the expected behavior.
                    return next();
                }

                // Preflight?
                // Typical preflight: OPTIONS + Access-Control-Request-Method
                if (session.Request.Method == "OPTIONS"
                    && session.Request.Headers.TryGetValue("Access-Control-Request-Method", out var reqMethod)
                    && !string.IsNullOrWhiteSpace(reqMethod)
                ) {

                    ApplyCommonCorsHeaders(session, origin, isPreflight: true);

                    // Preflight specific headers
                    string allowMethods = !string.IsNullOrWhiteSpace(_options.AllowedMethods)
                                            ? _options.AllowedMethods!
                                            : reqMethod.Trim();

                    session.Response.AddHeader("Access-Control-Allow-Methods", allowMethods);

                    // Allowed headers: either configured or echo requested
                    if (!string.IsNullOrWhiteSpace(_options.AllowedHeaders)) {
                        session.Response.AddHeader("Access-Control-Allow-Headers", _options.AllowedHeaders!);
                    }
                    else if (session.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var reqHeaders)
                             && !string.IsNullOrWhiteSpace(reqHeaders)
                    ) {
                        session.Response.AddHeader("Access-Control-Allow-Headers", reqHeaders.Trim());
                    }

                    if (_options.MaxAgeSeconds.HasValue && _options.MaxAgeSeconds.Value > 0) {
                        session.Response.AddHeader("Access-Control-Max-Age", _options.MaxAgeSeconds.Value.ToString());
                    }

                    // 204 No Content for preflight
                    return session.Response
                                  .Status(204)
                                  .AddHeader("Content-Length", "0")
                                  .SendAsync();
                }

                // Non-preflight: add headers and continue pipeline
                ApplyCommonCorsHeaders(session, origin, isPreflight: false);

                if (!string.IsNullOrWhiteSpace(_options.ExposedHeaders)) {
                    session.Response.AddHeader("Access-Control-Expose-Headers", _options.ExposedHeaders!);
                }

                return next();
            }

            /// <summary>
            /// Apply Common CORS Headers
            /// </summary>
            /// <param name="session"></param>
            /// <param name="origin"></param>
            /// <param name="isPreflight"></param>
            private void ApplyCommonCorsHeaders(HttpSession session, string origin, bool isPreflight) {

                // If AllowAnyOrigin and NOT credentials => "*"
                // If credentials => must echo origin (spec constraint)
                if (_options.AllowAnyOrigin && !_options.AllowCredentials) {
                    session.Response.AddHeader("Access-Control-Allow-Origin", "*");
                }
                else {
                    session.Response.AddHeader("Access-Control-Allow-Origin", origin);
                    // When echoing, it's a good practice to vary on Origin (shared caches)
                    session.Response.AddHeader("Vary", "Origin");
                }

                if (_options.AllowCredentials) {
                    session.Response.AddHeader("Access-Control-Allow-Credentials", "true");
                }

                // For preflight, it's also common to vary on Access-Control-Request-Headers / Method
                // (not mandatory, but cache correctness)
                if (isPreflight) {
                    session.Response.AddHeader("Vary", "Access-Control-Request-Method");
                    session.Response.AddHeader("Vary", "Access-Control-Request-Headers");
                }
            }

            /// <summary>
            /// Is Origin Allowed
            /// </summary>
            /// <param name="origin"></param>
            /// <returns></returns>
            private bool IsOriginAllowed(string origin) {
                if (_options.OriginValidator is not null) {
                    return _options.OriginValidator(origin);
                }

                if (_options.AllowAnyOrigin) {
                    return true;
                }

                return _allowedOrigins.Contains(origin);
            }

        }
    }

}
