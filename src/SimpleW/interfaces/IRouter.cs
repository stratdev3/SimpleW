namespace SimpleW {

    /// <summary>
    /// Interface for an HTTP router/dispatcher.
    /// </summary>
    public interface IRouter {

        /// <summary>
        /// Action to do on non-null handler results.
        /// </summary>
        HttpResultHandler ResultHandler { get; set; }

        /// <summary>
        /// Add a global middleware.
        /// </summary>
        void UseMiddleware(HttpMiddleware middleware);

        /// <summary>
        /// Map Method/Path to an executor.
        /// </summary>
        void Map(string method, string path, HttpRouteExecutor executor);

        /// <summary>
        /// Map Method/Path to an executor with handler metadata.
        /// </summary>
        void Map(string method, string path, HttpRouteExecutor executor, HandlerMetadataCollection metadata);

        /// <summary>
        /// Map Method/Host/Path to an executor.
        /// </summary>
        void Map(string method, string? host, string path, HttpRouteExecutor executor);

        /// <summary>
        /// Map Method/Host/Path to an executor with handler metadata.
        /// </summary>
        void Map(string method, string? host, string path, HttpRouteExecutor executor, HandlerMetadataCollection metadata);

        /// <summary>
        /// Map Method/Path to a delegate.
        /// </summary>
        void Map(string method, string path, Delegate handler);

        /// <summary>
        /// Map Method/Host/Path to a delegate.
        /// </summary>
        void Map(string method, string? host, string path, Delegate handler);

        /// <summary>
        /// Map GET/Path to a delegate.
        /// </summary>
        void MapGet(string path, Delegate handler);

        /// <summary>
        /// Map GET/Host/Path to a delegate.
        /// </summary>
        void MapGet(string host, string path, Delegate handler);

        /// <summary>
        /// Map POST/Path to a delegate.
        /// </summary>
        void MapPost(string path, Delegate handler);

        /// <summary>
        /// Map POST/Host/Path to a delegate.
        /// </summary>
        void MapPost(string host, string path, Delegate handler);

        /// <summary>
        /// Register the fallback handler.
        /// </summary>
        void MapFallback(Delegate handler);

        /// <summary>
        /// Dispatch one HTTP session.
        /// </summary>
        ValueTask DispatchAsync(HttpSession session);

        /// <summary>
        /// All declared Routes
        /// </summary>
        public IEnumerable<RouteInfo> Routes { get; }

    }

    /// <summary>
    /// Public route description for diagnostics, tooling, and documentation.
    /// </summary>
    public sealed class RouteInfo {

        /// <summary>
        /// HTTP method (GET, POST, ...)
        /// </summary>
        public string Method { get; }

        /// <summary>
        /// Optional host constraint
        /// </summary>
        public string? Host { get; }

        /// <summary>
        /// Route path/template
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Route IsAbsolutePath
        /// </summary>
        public bool IsAbsolutePath { get; }

        /// <summary>
        /// Optional description
        /// </summary>
        public string? Description { get; }

        /// <summary>
        /// IsPattern
        /// </summary>
        public bool IsPattern { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="method"></param>
        /// <param name="host"></param>
        /// <param name="path"></param>
        /// <param name="isAbsolutePath"></param>
        /// <param name="description"></param>
        /// <param name="isPattern"></param>
        public RouteInfo(string method, string? host, string path, bool isAbsolutePath, string? description, bool isPattern) {
            Method = method;
            Host = host;
            Path = path;
            IsAbsolutePath = isAbsolutePath;
            Description = description;
            IsPattern = isPattern;
        }

    }

}