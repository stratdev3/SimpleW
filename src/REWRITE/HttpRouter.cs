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
        /// Dictionary of all Routes
        /// </summary>
        private readonly Dictionary<(string method, string path), HttpRoute> _routes = new(StringTupleComparer.Ordinal);

        #region func

        /// <summary>
        /// Add Func content for method request
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public void Map(string method, string path, HttpHandler handler) {
            HttpRoute route = new(new HttpRouteAttribute(method, path), handler);
            _routes[(route.Attribute.Method, route.Attribute.Path)] = route;
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
            (string, string) key = (request.Method, request.Path);

            if (_routes.TryGetValue(key, out HttpRoute route)) {
                return route.Handler(session, request);
            }

            if (_fallback != null) {
                return _fallback(session, request);
            }

            return session.SendTextAsync("Not Found", 404, "Not Found");
        }

        #region helpers

        /// <summary>
        /// String Tuple Comparer for Route Dictionnary
        /// </summary>
        private sealed class StringTupleComparer : IEqualityComparer<(string, string)> {

            public static readonly StringTupleComparer Ordinal = new();

            public bool Equals((string, string) x, (string, string) y) => string.Equals(x.Item1, y.Item1, StringComparison.Ordinal)
                                                                          && string.Equals(x.Item2, y.Item2, StringComparison.Ordinal);

            public int GetHashCode((string, string) obj) {
                unchecked {
                    int hash = 17;
                    hash = hash * 31 + obj.Item1.GetHashCode();
                    hash = hash * 31 + obj.Item2.GetHashCode();
                    return hash;
                }
            }
        }

        #endregion helpers

    }

}
