namespace SimpleW {

    #region handlers for map delegate

    /// <summary>
    /// Executor for a Route
    /// </summary>
    public delegate ValueTask HttpRouteExecutor(HttpSession session, HttpHandlerResult handlerResult);

    #endregion handlers for map delegate

    #region special handlers

    /// <summary>
    /// Special Handler to handle non null result for HttpHandlerAsyncReturn/HttpHandlerSyncResult
    /// </summary>
    /// <param name="session"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    public delegate ValueTask HttpHandlerResult(HttpSession session, object result);

    /// <summary>
    /// Examples of HttpHandlerResult
    /// </summary>
    public static class HttpHandlerResults {

        /// <summary>
        /// Send Result as Json
        /// </summary>
        public static readonly HttpHandlerResult SendJsonResult = (session, result) => {
            return session.Response.Json(result).SendAsync();
        };

        /// <summary>
        /// Set Result as Json Body Response
        /// </summary>
        public static readonly HttpHandlerResult SetJsonBodyResponseResult = (session, result) => {
            session.Response.Json(result);
            return ValueTask.CompletedTask;
        };

        /// <summary>
        /// Do nothing
        /// </summary>
        public static readonly HttpHandlerResult DoNothingResult = (session, result) => {
            return ValueTask.CompletedTask;
        };

    }

    #endregion special handlers

    #region middleware

    /// <summary>
    /// Delegate for Middleware
    /// </summary>
    /// <param name="session"></param>
    /// <param name="next"></param>
    /// <returns></returns>
    public delegate ValueTask HttpMiddleware(HttpSession session, Func<ValueTask> next);

    #endregion middleware

}
