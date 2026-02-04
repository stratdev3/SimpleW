namespace SimpleW {

    #region handlers for map delegate

    /// <summary>
    /// Executor for a Route
    /// </summary>
    public delegate ValueTask HttpRouteExecutor(HttpSession session, HttpResultHandler resultHandler);

    #endregion handlers for map delegate

    #region special handlers

    /// <summary>
    /// Special Handler to handle non null result for HttpHandlerAsyncReturn/HttpHandlerSyncResult
    /// </summary>
    /// <param name="session"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    public delegate ValueTask HttpResultHandler(HttpSession session, object result);

    /// <summary>
    /// Examples of HttpResultHandler
    /// </summary>
    public static class HttpResultHandlers {

        /// <summary>
        /// Send Result as Json
        /// </summary>
        public static readonly HttpResultHandler SendJsonResult = (session, result) => {
            if (result is HttpResponse response) {
                // must be sure the response return result is the one of the current session !
                if (!ReferenceEquals(response, session.Response)) {
                    throw new InvalidOperationException("Returned HttpResponse is not session.Response");
                }
                return response.SendAsync();
            }
            // fallback
            return session.Response.Json(result).SendAsync();
        };

        /// <summary>
        /// Set Result as Json Body Response
        /// </summary>
        public static readonly HttpResultHandler SetJsonBodyResponseResult = (session, result) => {
            session.Response.Json(result);
            return ValueTask.CompletedTask;
        };

        /// <summary>
        /// Do nothing
        /// </summary>
        public static readonly HttpResultHandler DoNothingResult = (session, result) => {
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
