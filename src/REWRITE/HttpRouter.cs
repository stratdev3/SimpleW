namespace SimpleW {

    /// <summary>
    /// Delegate for MapGet() and MapPost()
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    public delegate ValueTask HttpHandler(HttpSession session);

    /// <summary>
    /// Delete for Middleware
    /// </summary>
    /// <param name="session"></param>
    /// <param name="next"></param>
    /// <returns></returns>
    public delegate ValueTask HttpMiddleware(HttpSession session, Func<ValueTask> next);

    /// <summary>
    /// HttpRouter
    /// </summary>
    public sealed class HttpRouter {

        /// <summary>
        /// Dictionary of GET/POST Routes
        /// </summary>
        private readonly Dictionary<string, HttpRoute> _get = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HttpRoute> _post = new(StringComparer.Ordinal);

        /// <summary>
        /// Dictionary of all others Routes (PATCH, HEAD, OPTIONS, etc.)
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, HttpRoute>> _others = new(StringComparer.Ordinal);

        #region middleware

        /// <summary>
        /// List of Middlewares
        /// </summary>
        private readonly List<HttpMiddleware> _middlewares = new();

        /// <summary>
        /// Add a new Middleware
        /// </summary>
        /// <param name="middleware"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <example>
        /// // add simple logging
        /// server.UseMiddleware(static async (session, next) => {
        ///     var sw = System.Diagnostics.Stopwatch.StartNew();
        ///     try {
        ///         await next();
        ///     }
        ///     finally {
        ///         sw.Stop();
        ///         Console.WriteLine($"[{DateTime.UtcNow:O}] {session.Request.Method} {session.Request.Path} in {sw.ElapsedMilliseconds} ms");
        ///     }
        /// });
        /// // add firewall/auth
        /// server.UseMiddleware(static (session, next) => {
        ///     if (session.Request.Path.StartsWith("/api/secret", StringComparison.Ordinal)) {
        ///         if (!session.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != "secret") {
        ///             return session.SendTextAsync("Unauthorized", 401, "Unauthorized");
        ///         }
        ///     }
        ///     return next();
        /// });
        /// </example>
        public void UseMiddleware(HttpMiddleware middleware) {
            ArgumentNullException.ThrowIfNull(middleware);
            _middlewares.Add(middleware);
        }

        /// <summary>
        /// Execute Middleware Pipeline
        /// </summary>
        /// <param name="session"></param>
        /// <param name="terminalHandler"></param>
        /// <returns></returns>
        private ValueTask ExecutePipelineAsync(HttpSession session, HttpHandler terminalHandler) {
            if (_middlewares.Count == 0) {
                return terminalHandler(session);
            }

            // build the next()
            Func<ValueTask> next = () => terminalHandler(session);

            // loop the next()
            for (int i = _middlewares.Count - 1; i >= 0; i--) {
                HttpMiddleware middleware = _middlewares[i];
                Func<ValueTask> localNext = next; // capture closure
                next = () => middleware(session, localNext);
            }

            return next();
        }

        #endregion middleware

        #region func

        /// <summary>
        /// Add Func content for method request
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void Map(string method, string path, HttpHandler handler) {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(handler);

            HttpRoute route = new(new HttpRouteAttribute(method, path), handler);

            switch (route.Attribute.Method) {
                case "GET":
                    _get[route.Attribute.Path] = route;
                    break;
                case "POST":
                    _post[route.Attribute.Path] = route;
                    break;
                default:
                    if (!_others.TryGetValue(route.Attribute.Method, out var dict)) {
                        dict = new Dictionary<string, HttpRoute>(StringComparer.Ordinal);
                        _others[route.Attribute.Method] = dict;
                    }
                    dict[route.Attribute.Path] = route;
                    break;
            }
        }

        /// <summary>
        /// Add Func content for GET request
        /// </summary>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void MapGet(string path, HttpHandler handler) => Map("GET", path, handler);

        /// <summary>
        /// Add Func content for POST request
        /// </summary>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void MapPost(string path, HttpHandler handler) => Map("POST", path, handler);

        #endregion func

        /// <summary>
        /// Fallback Handler
        /// </summary>
        private HttpHandler? _fallback;

        /// <summary>
        /// Set Fallback Handler
        /// </summary>
        /// <param name="handler"></param>
        public void MapFallback(HttpHandler handler) => _fallback = handler;

        /// <summary>
        /// Find Handler from Method/Path
        /// </summary>
        /// <param name="session"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        public ValueTask DispatchAsync(HttpSession session) {
            HttpRoute? route;

            Dictionary<string, HttpRoute>? dict = session.Request.Method switch {
                "GET" => _get,
                "POST" => _post,
                _ => null
            };

            if (dict is not null && dict.TryGetValue(session.Request.Path, out route)) {
                return ExecutePipelineAsync(session, route.Handler);
            }

            if (dict is null) {
                if (_others.TryGetValue(session.Request.Method, out Dictionary<string, HttpRoute>? otherDict)
                    && otherDict.TryGetValue(session.Request.Path, out route)
                ) {
                    return ExecutePipelineAsync(session, route.Handler);
                }
            }

            if (_fallback != null) {
                return ExecutePipelineAsync(session, _fallback);
            }

            return ExecutePipelineAsync(session, static (session) => session.SendTextAsync("Not Found", 404, "Not Found"));
        }

    }

}
