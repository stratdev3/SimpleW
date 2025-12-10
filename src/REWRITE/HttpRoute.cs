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
        /// The Handler
        /// </summary>
        public HttpHandlerVoid Handler { get; internal set; }

        /// <summary>
        /// Create HttpRoute from RouteAttribute
        /// </summary>
        /// <param name="attribute">the HttpRoute attribute.</param>
        /// <param name="handler">the HttpHandler.</param>
        public HttpRoute(HttpRouteAttribute attribute, HttpHandlerVoid handler) {
            Attribute = attribute;
            Handler = handler;
        }

    }

}
