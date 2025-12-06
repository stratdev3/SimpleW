namespace SimpleW {

    /// <summary>
    /// Delegate for MapGet() and MapPost()
    /// </summary>
    /// <param name="session"></param>
    /// <param name="request"></param>
    /// <returns></returns>
    public delegate ValueTask HttpHandler(HttpSession session, HttpRequest request);

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

        #region func

        /// <summary>
        /// Add Func content for method request
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void Map(string method, string path, HttpHandler handler) {
            if (method is null) {
                throw new ArgumentNullException(nameof(method));
            }
            if (path is null) {
                throw new ArgumentNullException(nameof(path));
            }
            if (handler is null) {
                throw new ArgumentNullException(nameof(handler));
            }

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
        public ValueTask DispatchAsync(HttpSession session, HttpRequest request) {
            HttpRoute? route;

            Dictionary<string, HttpRoute>? dict = request.Method switch {
                "GET" => _get,
                "POST" => _post,
                _ => null
            };

            if (dict is not null && dict.TryGetValue(request.Path, out route)) {
                return route.Handler(session, request);
            }

            if (dict is null) {
                if (_others.TryGetValue(request.Method, out Dictionary<string, HttpRoute>? otherDict) && otherDict.TryGetValue(request.Path, out route)) {
                    return route.Handler(session, request);
                }
            }

            if (_fallback != null) {
                return _fallback(session, request);
            }

            return session.SendTextAsync("Not Found", 404, "Not Found");
        }

    }

}
