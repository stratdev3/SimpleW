namespace SimpleW {

    /// <summary>
    /// HttpRoute
    /// </summary>
    public sealed class Route {

        /// <summary>
        /// The RouteAttribute
        /// </summary>
        public readonly RouteAttribute Attribute;

        /// <summary>
        /// Executor
        /// </summary>
        public HttpRouteExecutor Executor { get; }

        /// <summary>
        /// Create HttpRoute from RouteAttribute
        /// </summary>
        /// <param name="attribute">the HttpRoute attribute.</param>
        /// <param name="executor"></param>
        public Route(RouteAttribute attribute, HttpRouteExecutor executor) {
            Attribute = attribute;
            Executor = executor;
        }

    }

}
