namespace SimpleW {

    /// <summary>
    /// Router
    /// </summary>
    public sealed class Router {

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
                return terminalExecutor(session, HandlerResult);
            }

            // build the next()
            Func<ValueTask> next = () => terminalExecutor(session, HandlerResult);

            // loop the next()
            for (int i = _middlewares.Count - 1; i >= 0; i--) {
                HttpMiddleware middleware = _middlewares[i];
                Func<ValueTask> localNext = next; // capture closure
                next = () => middleware(session, localNext);
            }

            return next();
        }

        #endregion middleware

        #region HandlerResult

        /// <summary>
        /// Action to do on the non null Result of any handler (Delegate).
        /// </summary>
        public HttpHandlerResult HandlerResult { get; set; } = HttpHandlerResults.SendJsonResult;

        #endregion HandlerResult

        #region Map Method Path Delegate

        /// <summary>
        /// Map Method/Path to a Delegate
        /// </summary>
        /// <param name="method"></param>
        /// <param name="path"></param>
        /// <param name="handler"></param>
        public void Map(string method, string path, Delegate handler) {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(handler);

            HttpRouteExecutor executor = DelegateExecutorFactory.Create(handler);
            Route route = new(new RouteAttribute(method, path), executor);

            AddRouteInternal(route);
        }

        /// <summary>
        /// Map GET/Path to a Delegate
        /// </summary>
        public void MapGet(string path, Delegate handler) => Map("GET", path, handler);

        /// <summary>
        /// Map POST/Path to a Delegate
        /// </summary>
        public void MapPost(string path, Delegate handler) => Map("POST", path, handler);

        /// <summary>
        /// AddRouteInternal
        /// </summary>
        /// <param name="route"></param>
        private void AddRouteInternal(Route route) {
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

        #endregion Map Method Path Delegate

        /// <summary>
        /// A.) Find Handler from Method/Path with the following priority :
        ///     1. find exact route for GET/POST method, O(1) complexity, goto B
        ///     2. if not found, find exact route for other methods, O(n) complexity, goto B
        ///     3. if not found, find pattern route for GET/POST method, goto B
        ///     4. if not found, find pattern route for other methods, goto B
        ///     5. if not found, execute fallback handler
        ///     6. if not fallback handler, send a 404 response
        ///     
        /// B.) Once the Handler is found, Execute with the following pipeline :
        ///     1. fast path if no middleware : execute Handler
        ///     2. loop to all middleware until next()
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public ValueTask DispatchAsync(HttpSession session) {
            ValueTask task;

            // exact routes
            if (TryDispatchExact(session, out task)) {
                return task;
            }

            // pattern routes (params + wildcard)
            if (TryDispatchPattern(session, out task)) {
                return task;
            }

            // fallback
            if (_fallback != null) {
                return ExecutePipelineAsync(session, _fallback);
            }

            // at last, return a 404
            return ExecutePipelineAsync(
                session,
                DelegateExecutorFactory.Create(static (HttpSession s) => s.Response.Status(404).Text("Not Found").SendAsync())
            );
        }

        /// <summary>
        /// Find Handler in route exact
        /// </summary>
        /// <param name="session"></param>
        /// <param name="task"></param>
        /// <returns></returns>
        private bool TryDispatchExact(HttpSession session, out ValueTask task) {
            task = default;

            Route? route;

            Dictionary<string, Route>? dict = session.Request.Method switch {
                "GET" => _get,
                "POST" => _post,
                _ => null
            };

            // GET / POST exact
            if (dict is not null && dict.TryGetValue(session.Request.Path, out route)) {
                task = ExecutePipelineAsync(session, route.Executor);
                return true;
            }

            // other methods exact
            if (dict is null
                && _others.TryGetValue(session.Request.Method, out Dictionary<string, Route>? otherDict)
                && otherDict.TryGetValue(session.Request.Path, out route)
            ) {
                task = ExecutePipelineAsync(session, route.Executor);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find Handler in route pattern
        /// </summary>
        /// <param name="session"></param>
        /// <param name="task"></param>
        /// <returns></returns>
        private bool TryDispatchPattern(HttpSession session, out ValueTask task) {
            task = default;

            // router owns this data
            session.Request.ParserSetRouteValues(null);

            List<RouteMatcher>? matchers = session.Request.Method switch {
                "GET" => _getMatchers,
                "POST" => _postMatchers,
                _ => _otherMatchers.TryGetValue(session.Request.Method, out var l) ? l : null
            };

            if (matchers is null || matchers.Count == 0) {
                return false;
            }

            RouteMatcher? best = null;
            Dictionary<string, string>? bestValues = null;

            string path = session.Request.Path;

            for (int i = 0; i < matchers.Count; i++) {
                RouteMatcher m = matchers[i];

                if (!m.TryMatch(path, out Dictionary<string, string>? values)) {
                    continue;
                }

                if (best is null || m.Specificity > best.Specificity) {
                    best = m;
                    bestValues = values;
                }
            }

            if (best is null) {
                return false;
            }

            session.Request.ParserSetRouteValues(bestValues);
            task = ExecutePipelineAsync(session, best.Route.Executor);
            return true;
        }

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
            _fallback = DelegateExecutorFactory.Create(handler);
        }

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

                    string s = seg.ToString(); // OK ici : compile-time, une seule fois au Map()
                    var compiled = new Segment(s);
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

        /// <summary>
        /// All declared Routes
        /// </summary>
        public IEnumerable<RouteInfo> Routes {
            get {
                List<RouteInfo> routes = new();

                static RouteInfo ToInfo(Route r, bool isPattern) => new(
                    r.Attribute.Method,
                    r.Attribute.Path,
                    r.Attribute.IsAbsolutePath,
                    r.Attribute.Description,
                    isPattern
                );

                // exact routes
                foreach (var r in _get.Values) {
                    routes.Add(ToInfo(r, isPattern: false));
                }
                foreach (var r in _post.Values) {
                    routes.Add(ToInfo(r, isPattern: false));
                }
                foreach (var dict in _others.Values) {
                    foreach (var r in dict.Values) {
                        routes.Add(ToInfo(r, isPattern: false));
                    }
                }

                // pattern routes
                foreach (var m in _getMatchers) {
                    routes.Add(ToInfo(m.Route, isPattern: true));
                }
                foreach (var m in _postMatchers) {
                    routes.Add(ToInfo(m.Route, isPattern: true));
                }
                foreach (var list in _otherMatchers.Values) {
                    foreach (var m in list) {
                        routes.Add(ToInfo(m.Route, isPattern: true));
                    }
                }

                return routes;
            }
        }

        /// <summary>
        /// Route Info
        /// </summary>
        /// <param name="Method"></param>
        /// <param name="Path"></param>
        /// <param name="IsAbsolutePath"></param>
        /// <param name="Description"></param>
        /// <param name="IsPattern"></param>
        public sealed record RouteInfo(string Method, string Path, bool IsAbsolutePath, string? Description, bool IsPattern);

    }

}
