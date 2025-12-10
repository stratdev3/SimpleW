namespace SimpleW {

    #region handlers for map func

    /// <summary>
    /// Delegate for handler that send the response on their own (ValueTask)
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    /// <example>
    /// server.MapGet("/api/test/text", static (session) => {
    ///     return session.SendTextAsync("Hello World !");
    /// });
    /// server.MapGet("/api/test/hello", static async (session) => {
    ///     await session.SendJsonAsync(new { message = "Hello World !" });
    /// });
    /// </example>
    public delegate ValueTask HttpHandlerVoid(HttpSession session);

    /// <summary>
    /// Delegate for async handler that return a result ValueTask<object?>
    /// The return object will be automatically serialized to json and sent
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    /// <example>
    /// server.MapGet("/api/test/hello", static async (session) => {
    ///     await Task.Delay(2_000);
    ///     return new { message = "Hello World !" };
    /// });
    /// </example>
    public delegate ValueTask<object?> HttpHandlerAsyncReturn(HttpSession session);

    /// <summary>
    /// Delegate for sync handler that return a result object?
    /// The return object will be automatically serialized to json and sent
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    /// <example>
    /// server.MapGet("/api/test/text", static (session) => {
    ///     return session.SendTextAsync("Hello World !");
    /// });
    /// </example>
    public delegate object? HttpHandlerSyncResult(HttpSession session);

    #endregion handlers for map func

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
            return session.SendJsonAsync(result);
        };

        /// <summary>
        /// Do nothing with the Result
        /// </summary>
        public static readonly HttpHandlerResult DoNothingWithResult = (session, result) => {
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
