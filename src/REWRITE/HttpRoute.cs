namespace SimpleW {

    /// <summary>
    /// HttpRoute
    /// </summary>
    public sealed class HttpRoute {

        /// <summary>
        /// The RouteAttribute
        /// </summary>
        public readonly HttpRouteAttribute Attribute;

        /// <summary>
        /// Executor
        /// </summary>
        public HttpRouteExecutor Executor { get; }

        /// <summary>
        /// Create HttpRoute from RouteAttribute
        /// </summary>
        /// <param name="attribute">the HttpRoute attribute.</param>
        /// <param name="executor"></param>
        public HttpRoute(HttpRouteAttribute attribute, HttpRouteExecutor executor) {
            Attribute = attribute;
            Executor = executor;
        }

    }

}
