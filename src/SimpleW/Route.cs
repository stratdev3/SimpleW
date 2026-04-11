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
        /// Immutable metadata attached to the route handler.
        /// </summary>
        public HandlerMetadataCollection Metadata { get; }

        /// <summary>
        /// Create HttpRoute from RouteAttribute
        /// </summary>
        /// <param name="attribute">the HttpRoute attribute.</param>
        /// <param name="executor"></param>
        /// <param name="metadata"></param>
        public Route(RouteAttribute attribute, HttpRouteExecutor executor, HandlerMetadataCollection? metadata = null) {
            Attribute = attribute;
            Executor = executor;
            Metadata = metadata ?? HandlerMetadataCollection.Empty;
        }

    }

}
