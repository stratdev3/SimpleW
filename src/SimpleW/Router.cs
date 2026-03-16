namespace SimpleW {

    /// <summary>
    /// Router
    /// </summary>
    public sealed class Router {

        #region constructor

        /// <summary>
        /// Is Root Router
        /// </summary>
        private readonly bool _isRootRouter;

        /// <summary>
        /// Global Router
        /// </summary>
        private readonly Router? _globalRouter;

        /// <summary>
        /// Host Routers
        /// </summary>
        private readonly Dictionary<string, Router>? _hostRouters;

        /// <summary>
        /// Constructor
        /// </summary>
        public Router() : this(isRootRouter: true) { }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="isRootRouter"></param>
        private Router(bool isRootRouter) {
            _isRootRouter = isRootRouter;

            if (isRootRouter) {
                _globalRouter = new Router(isRootRouter: false);
                _globalRouter._resultHandler = _resultHandler;

                _hostRouters = new Dictionary<string, Router>(StringComparer.Ordinal);
            }
        }

        #endregion constructor

        #region route exact

        /// <summary>
        /// GET Routes
        /// </summary>
        private readonly Dictionary<string, Route> _get = new(StringComparer.Ordinal);

        /// <summary>
        /// POST Routes
        /// </summary>
        private readonly Dictionary<string, Route> _post = new(StringComparer.Ordinal);

        /// <summary>
        /// (PATCH, HEAD, OPTIONS, etc.) Routes
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, Route>> _others = new(StringComparer.Ordinal);

        #endregion route exact

        #region route pattern

        /// <summary>
        /// GET Routes patterns (wildcards + params)
        /// </summary>
        private readonly List<RouteMatcher> _getMatchers = new();

        /// <summary>
        /// POST Routes patterns (wildcards + params)
        /// </summary>
        private readonly List<RouteMatcher> _postMatchers = new();

        /// <summary>
        /// (PATCH, HEAD, OPTIONS, etc.) Routes (wildcards + params)
        /// </summary>
        private readonly Dictionary<string, List<RouteMatcher>> _otherMatchers = new(StringComparer.Ordinal);

        #endregion route pattern

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
            if (!_isRootRouter) {
                throw new InvalidOperationException("Middlewares must be registered on the root router only.");
            }
            _middlewares.Add(middleware);
        }

        /// <summary>
        /// Execute Pipeline
        /// </summary>
        /// <param name="session"></param>
        /// <param name="terminalExecutor"></param>
        /// <returns></returns>
        private ValueTask ExecutePipelineAsync(HttpSession session, HttpRouteExecutor terminalExecutor) {
            if (_middlewares.Count == 0) {
                return terminalExecutor(session, ResultHandler);
            }

            // build the next()
            Func<ValueTask> next = () => terminalExecutor(session, ResultHandler);

            // loop the next()
            for (int i = _middlewares.Count - 1; i >= 0; i--) {
                HttpMiddleware middleware = _middlewares[i];
                Func<ValueTask> localNext = next; // capture closure
                next = () => middleware(session, localNext);
            }

            return next();
        }

        #endregion middleware

        #region ResultHandler

        private HttpResultHandler _resultHandler = HttpResultHandlers.SendJsonResult;

        /// <summary>
        /// Action to do on the non null Result of any handler (Delegate).
        /// </summary>
        public HttpResultHandler ResultHandler {
            get => _resultHandler;
            set {
                _resultHandler = value;

                if (_isRootRouter) {
                    _globalRouter!._resultHandler = value;

                    foreach (Router child in _hostRouters!.Values) {
                        child._resultHandler = value;
                    }
                }
            }
        }

        #endregion ResultHandler

        #region Map Method

        /// <summary>
        /// Map Method/Path to a HttpRouteExecutor
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="executor"></param>
        public void Map(string method, string path, HttpRouteExecutor executor) {
            Map(method, host: null, path, executor);
        }

        /// <summary>
        /// Map Method/Host/Path to a HttpRouteExecutor
        /// </summary>
        /// <param name="method"></param>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="executor"></param>
        public void Map(string method, string? host, string path, HttpRouteExecutor executor) {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(executor);

            Route route = new(
                string.IsNullOrWhiteSpace(host)
                    ? new RouteAttribute(method, path)
                    : new RouteAttribute(method, path) { Host = host },
                executor
            );

            AddRouteInternal(route);
        }

        /// <summary>
        /// Map Method/Host/Path to a Delegate
        /// </summary>
        /// <param name="method"></param>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        public void Map(string method, string? host, string path, Delegate handler) {
            Map(method, host, path, RouteExecutorFactory.Create(handler));
        }

        /// <summary>
        /// Map Method/Path to a Delegate
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        public void Map(string method, string path, Delegate handler) {
            Map(method, host: null, path, handler);
        }

        /// <summary>
        /// Map GET/Path to a Delegate
        /// </summary>
        public void MapGet(string path, Delegate handler) => Map("GET", path, handler);

        /// <summary>
        /// Map GET/Host/Path to a Delegate
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        public void MapGet(string host, string path, Delegate handler) => Map("GET", host, path, handler);

        /// <summary>
        /// Map POST/Path to a Delegate
        /// </summary>
        public void MapPost(string path, Delegate handler) => Map("POST", path, handler);

        /// <summary>
        /// Map POST/Host/Path to a Delegate
        /// </summary>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        public void MapPost(string host, string path, Delegate handler) => Map("POST", host, path, handler);

        #endregion Map Method

        #region Add Route

        /// <summary>
        /// AddRouteInternal
        /// </summary>
        /// <param name="route"></param>
        private void AddRouteInternal(Route route) {
            string p = route.Attribute.Path;
            ValidateRoutePath(p);

            if (_isRootRouter) {
                string? host = NormalizeHost(route.Attribute.Host);

                // global router
                if (host == null) {
                    _globalRouter!.AddRouteLocal(route);
                    return;
                }
                // host router
                else {
                    // create if not exists
                    if (!_hostRouters!.TryGetValue(host, out Router? hostRouter)) {
                        hostRouter = new Router(isRootRouter: false) { _resultHandler = _resultHandler };
                        _hostRouters[host] = hostRouter;
                    }
                    // rewrite the route and add it to the host router
                    hostRouter.AddRouteLocal(new Route(
                        new RouteAttribute(route.Attribute.Method, route.Attribute.Path) {
                            IsAbsolutePath = route.Attribute.IsAbsolutePath,
                            Description = route.Attribute.Description
                        },
                        route.Executor
                    ));
                    return;
                }
            }

            AddRouteLocal(route);
        }

        /// <summary>
        /// AddRouteLocal depending on
        ///     - exact route
        ///     - pattern route
        /// </summary>
        /// <param name="route"></param>
        private void AddRouteLocal(Route route) {
            string p = route.Attribute.Path;
            bool isPattern = p.IndexOf('*') >= 0 || p.IndexOf(':') >= 0;

            if (!isPattern) {
                switch (route.Attribute.Method) {
                    case "GET":
                        _get[p] = route;
                        return;
                    case "POST":
                        _post[p] = route;
                        return;
                    default:
                        if (!_others.TryGetValue(route.Attribute.Method, out var dict)) {
                            dict = new Dictionary<string, Route>(StringComparer.Ordinal);
                            _others[route.Attribute.Method] = dict;
                        }
                        dict[p] = route;
                        return;
                }
            }

            // pattern
            RouteMatcher matcher = new(route);

            switch (route.Attribute.Method) {
                case "GET":
                    _getMatchers.Add(matcher);
                    return;
                case "POST":
                    _postMatchers.Add(matcher);
                    return;
                default:
                    if (!_otherMatchers.TryGetValue(route.Attribute.Method, out var list)) {
                        list = new List<RouteMatcher>();
                        _otherMatchers[route.Attribute.Method] = list;
                    }
                    list.Add(matcher);
                    return;
            }
        }

        /// <summary>
        /// Validate Route Path
        /// </summary>
        /// <param name="path"></param>
        /// <exception cref="ArgumentException"></exception>
        private static void ValidateRoutePath(string path) {
            // no double slash
            if (path.Contains("//", StringComparison.Ordinal)) {
                throw new ArgumentException($"Invalid route path '{path}'. Double slashes '//' are not allowed.", nameof(path));
            }
        }

        /// <summary>
        /// Normalize Host
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private static string? NormalizeHost(string? host) {
            if (string.IsNullOrWhiteSpace(host)) {
                return null;
            }

            host = host.Trim();

            // IPv6 bracket form: [::1]:8080
            if (host.Length > 0 && host[0] == '[') {
                int endBracket = host.IndexOf(']');
                if (endBracket > 0) {
                    return host.Substring(0, endBracket + 1).ToLowerInvariant();
                }

                return host.ToLowerInvariant();
            }

            // host:port
            int colonIndex = host.LastIndexOf(':');
            if (colonIndex > 0) {
                host = host.Substring(0, colonIndex);
            }

            return host.ToLowerInvariant();
        }

        #endregion Add Route

        #region Dispatch Route

        /// <summary>
        /// Main Dispatch :
        ///     1. try find the route in a host router, and Execute
        ///     2. try find the route in the global router, and Execute
        ///     3. else fallback, and Execute
        ///     4. 404 and Execute
        ///
        /// The Execute is the following :
        ///     1. fast path if no middleware : execute Handler
        ///     2. loop to all middleware until next()
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public ValueTask DispatchAsync(HttpSession session) {
            if (!_isRootRouter) {
                throw new InvalidOperationException("DispatchAsync must be called on the root router only.");
            }

            string? host = NormalizeHost(session.Request.Headers.Host);

            // 1. host routes
            if (host != null
                && _hostRouters!.TryGetValue(host, out Router? hostRouter)
                && hostRouter.TryResolveLocal(session, out HttpRouteExecutor? hostExecutor) && hostExecutor != null
            ) {
                return ExecutePipelineAsync(session, hostExecutor);
            }

            // 2. global routes
            if (_globalRouter!.TryResolveLocal(session, out HttpRouteExecutor? globalExecutor) && globalExecutor != null) {
                return ExecutePipelineAsync(session, globalExecutor);
            }

            // 3. fallback
            if (_fallback != null) {
                session.Request.ParserSetRouteTemplate(":fallback");
                return ExecutePipelineAsync(session, _fallback);
            }

            // 4. at last, return a 404
            session.Request.ParserSetRouteTemplate(":notfound");
            return ExecutePipelineAsync(session,_notFoundExecutor);
        }

        /// <summary>
        /// Find Handler from Method/Path
        /// </summary>
        /// <param name="session"></param>
        /// <param name="executor"></param>
        /// <returns></returns>
        private bool TryResolveLocal(HttpSession session, out HttpRouteExecutor? executor) {
            executor = null;

            if (TryResolveLocalExact(session, out executor)) {
                return true;
            }

            if (TryResolveLocalPattern(session, out executor)) {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find Handler from Method/Path with exact route, with the following priority :
        ///     1. find exact route for GET/POST method, O(1) complexity
        ///     2. if not found, find exact route for other methods, O(n) complexity
        /// </summary>
        /// <param name="session"></param>
        /// <param name="executor"></param>
        /// <returns></returns>
        private bool TryResolveLocalExact(HttpSession session, out HttpRouteExecutor? executor) {
            executor = null;

            Route? route;

            Dictionary<string, Route>? dict = session.Request.Method switch {
                "GET" => _get,
                "POST" => _post,
                _ => null
            };

            // GET / POST exact
            if (dict != null && dict.TryGetValue(session.Request.Path, out route)) {
                session.Request.ParserSetRouteTemplate(route.Attribute.Path);
                executor = route.Executor;
                return true;
            }

            // other methods exact
            if (dict == null
                && _others.TryGetValue(session.Request.Method, out Dictionary<string, Route>? otherDict)
                && otherDict.TryGetValue(session.Request.Path, out route)
            ) {
                session.Request.ParserSetRouteTemplate(route.Attribute.Path);
                executor = route.Executor;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find Handler from Method/Path with pattern route, with the following priority :
        ///     1. find pattern route for GET/POST method, O(n) complexity
        ///     2. if not found, find pattern route for other methods, O(n) complexity
        /// </summary>
        /// <param name="session"></param>
        /// <param name="executor"></param>
        /// <returns></returns>
        private bool TryResolveLocalPattern(HttpSession session, out HttpRouteExecutor? executor) {
            executor = null;

            // router owns this data
            session.Request.ParserSetRouteValues(null);

            List<RouteMatcher>? matchers = session.Request.Method switch {
                "GET" => _getMatchers,
                "POST" => _postMatchers,
                _ => _otherMatchers.TryGetValue(session.Request.Method, out var l) ? l : null
            };

            if (matchers == null || matchers.Count == 0) {
                return false;
            }

            RouteMatcher? best = null;
            Dictionary<string, string>? bestValues = null;

            foreach (RouteMatcher matcher in matchers) {
                if (!matcher.TryMatch(session.Request.Path, out Dictionary<string, string>? values)) {
                    continue;
                }

                if (best == null || matcher.Specificity > best.Specificity) {
                    best = matcher;
                    bestValues = values;
                }
            }

            if (best == null) {
                return false;
            }

            session.Request.ParserSetRouteValues(bestValues);
            session.Request.ParserSetRouteTemplate(best.Route.Attribute.Path);
            executor = best.Route.Executor;
            return true;
        }

        #endregion Dispatch Route

        #region fallback

        /// <summary>
        /// Fallback Handler
        /// </summary>
        private HttpRouteExecutor? _fallback;

        /// <summary>
        /// Set Fallback Handler
        /// </summary>
        /// <param name="handler"></param>
        public void MapFallback(Delegate handler) {
            ArgumentNullException.ThrowIfNull(handler);
            if (!_isRootRouter) {
                throw new InvalidOperationException("Fallback must be registered on the root router only.");
            }
            _fallback = RouteExecutorFactory.Create(handler);
        }

        #endregion fallback

        #region not found

        private static readonly HttpRouteExecutor _notFoundExecutor = RouteExecutorFactory.Create(static (HttpSession s) => s.Response.Status(404).Text("Not Found").SendAsync());

        #endregion not found

        #region RouteMatcher

        /// <summary>
        /// RouteMatcher (segments + :params + * + :param*)
        /// </summary>
        private sealed class RouteMatcher {

            public readonly Route Route;
            private readonly Segment[] _segments;
            public readonly int Specificity;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="route"></param>
            public RouteMatcher(Route route) {
                Route = route;

                // pattern compile once (alloc OK here: called on Map, not per request)
                string pattern = route.Attribute.Path;
                ReadOnlySpan<char> p = pattern.AsSpan();

                // count segments without Split
                int count = CountSegments(p);
                _segments = new Segment[count];

                int segIndex = 0;
                int spec = 0;

                int i = 0;
                SkipSlashes(p, ref i);

                while (i < p.Length) {
                    int start = i;
                    i = IndexOfSlashOrEnd(p, i);

                    ReadOnlySpan<char> seg = p.Slice(start, i - start);

                    string s = seg.ToString();
                    Segment compiled = new(s);
                    _segments[segIndex++] = compiled;

                    if (!compiled.IsParam && !compiled.IsWildcard) {
                        spec += compiled.LiteralOrName.Length;
                    }

                    SkipSlashes(p, ref i);
                }

                Specificity = spec;
            }

            /// <summary>
            /// Find parameters in path
            /// </summary>
            /// <param name="path"></param>
            /// <param name="values"></param>
            /// <returns></returns>
            public bool TryMatch(string path, out Dictionary<string, string>? values) {
                values = null;

                ReadOnlySpan<char> p = path.AsSpan();

                int segPos = 0;     // position in path
                int segIndex = 0;   // index in pattern segments

                // iterate pattern segments and compare to path segments (streaming)
                while (true) {
                    if (segIndex >= _segments.Length) {
                        // pattern ended: path must also end (no remaining non-empty segments)
                        SkipSlashes(p, ref segPos);
                        return segPos >= p.Length;
                    }

                    ref readonly Segment pat = ref _segments[segIndex];

                    // wildcard segment "*" matches the rest (including empty)
                    if (pat.IsWildcard) {
                        return true;
                    }

                    // read next request segment
                    SkipSlashes(p, ref segPos);
                    if (segPos >= p.Length) {
                        return false; // pattern expects more, path ended
                    }

                    int segStart = segPos;
                    int segEnd = IndexOfSlashOrEnd(p, segPos);
                    ReadOnlySpan<char> reqSeg = p.Slice(segStart, segEnd - segStart);
                    segPos = segEnd;

                    if (pat.IsParam) {
                        values ??= new Dictionary<string, string>(StringComparer.Ordinal);

                        if (pat.IsCatchAll) {
                            // catch-all gets current segment + the rest (without allocating intermediates)
                            // include current segment + whatever remains (including slashes)
                            int catchStart = segStart;
                            // also trim leading '/' if any (shouldn't happen because we skipped slashes)
                            ReadOnlySpan<char> rest = p.Slice(catchStart);

                            // drop trailing slashes
                            int end = rest.Length;
                            while (end > 0 && rest[end - 1] == '/') {
                                end--;
                            }

                            values[pat.LiteralOrName] = rest.Slice(0, end).ToString();
                            return true;
                        }

                        // normal param: allocate only this captured segment
                        values[pat.LiteralOrName] = reqSeg.ToString();
                        segIndex++;
                        continue;
                    }

                    // literal compare without allocations
                    if (!reqSeg.Equals(pat.LiteralOrName.AsSpan(), StringComparison.Ordinal)) {
                        return false;
                    }

                    segIndex++;
                }
            }

            #region helpers

            private static void SkipSlashes(ReadOnlySpan<char> s, ref int i) {
                while (i < s.Length && s[i] == '/') {
                    i++;
                }
            }

            private static int IndexOfSlashOrEnd(ReadOnlySpan<char> s, int start) {
                int i = start;
                while (i < s.Length && s[i] != '/') {
                    i++;
                }
                return i;
            }

            private static int CountSegments(ReadOnlySpan<char> path) {
                int i = 0;
                int count = 0;

                SkipSlashes(path, ref i);

                while (i < path.Length) {
                    count++;
                    i = IndexOfSlashOrEnd(path, i);
                    SkipSlashes(path, ref i);
                }

                return count;
            }

            #endregion helpers

            /// <summary>
            /// Segment
            /// </summary>
            private readonly struct Segment {

                public readonly string LiteralOrName;   // literal OR param name
                public readonly bool IsParam;
                public readonly bool IsWildcard;        // "*" segment
                public readonly bool IsCatchAll;        // ":name*"

                public Segment(string raw) {
                    IsWildcard = (raw == "*");
                    IsParam = (raw.Length > 1 && raw[0] == ':');
                    IsCatchAll = (IsParam && raw.EndsWith("*", StringComparison.Ordinal));

                    LiteralOrName = (IsParam ? raw.TrimStart(':').TrimEnd('*') : raw);
                }
            }

        }

        #endregion RouteMatcher

        #region list/export Routes

        /// <summary>
        /// All declared Routes
        /// </summary>
        public IEnumerable<RouteInfo> Routes {
            get {
                List<RouteInfo> routes = new();

                if (_isRootRouter) {
                    // global routes
                    if (_globalRouter != null) {
                        foreach (RouteInfo route in _globalRouter.GetLocalRoutes(host: null)) {
                            routes.Add(route);
                        }
                    }

                    // host routes
                    if (_hostRouters != null) {
                        foreach (KeyValuePair<string, Router> kv in _hostRouters) {
                            string host = kv.Key;
                            Router hostRouter = kv.Value;

                            foreach (RouteInfo route in hostRouter.GetLocalRoutes(host)) {
                                routes.Add(route);
                            }
                        }
                    }

                    return routes;
                }

                // child/global router: only local routes
                return GetLocalRoutes(host: null);
            }
        }

        /// <summary>
        /// Get local routes from the current router only
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        private List<RouteInfo> GetLocalRoutes(string? host) {
            List<RouteInfo> routes = [];

            RouteInfo ToInfo(Route r, bool isPattern) => new(
                r.Attribute.Method,
                host,
                r.Attribute.Path,
                r.Attribute.IsAbsolutePath,
                r.Attribute.Description,
                isPattern
            );

            // exact routes
            foreach (Route r in _get.Values) {
                routes.Add(ToInfo(r, isPattern: false));
            }

            foreach (Route r in _post.Values) {
                routes.Add(ToInfo(r, isPattern: false));
            }

            foreach (Dictionary<string, Route> dict in _others.Values) {
                foreach (Route r in dict.Values) {
                    routes.Add(ToInfo(r, isPattern: false));
                }
            }

            // pattern routes
            foreach (RouteMatcher m in _getMatchers) {
                routes.Add(ToInfo(m.Route, isPattern: true));
            }

            foreach (RouteMatcher m in _postMatchers) {
                routes.Add(ToInfo(m.Route, isPattern: true));
            }

            foreach (List<RouteMatcher> list in _otherMatchers.Values) {
                foreach (RouteMatcher m in list) {
                    routes.Add(ToInfo(m.Route, isPattern: true));
                }
            }

            return routes;
        }

        /// <summary>
        /// Route Info
        /// </summary>
        /// <param name="Method"></param>
        /// <param name="Host"></param>
        /// <param name="Path"></param>
        /// <param name="IsAbsolutePath"></param>
        /// <param name="Description"></param>
        /// <param name="IsPattern"></param>
        public sealed record RouteInfo(string Method, string? Host, string Path, bool IsAbsolutePath, string? Description, bool IsPattern);

        #endregion list/export Routes

    }

}
