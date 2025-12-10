namespace SimpleW {

    /// <summary>
    /// Delegate for handler that send the response on their own (ValueTask)
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    public delegate ValueTask HttpHandlerVoid(HttpSession session);

    /// <summary>
    /// Delegate for async handler that return a result ValueTask<object?>
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    public delegate ValueTask<object?> HttpHandlerAsyncReturn(HttpSession session);

    /// <summary>
    /// Delegate for sync handler that return a result object?
    /// </summary>
    /// <param name="session"></param>
    /// <returns></returns>
    public delegate object? HttpHandlerSyncResult(HttpSession session);

    /// <summary>
    /// Delete for Middleware
    /// </summary>
    /// <param name="session"></param>
    /// <param name="next"></param>
    /// <returns></returns>
    public delegate ValueTask HttpMiddleware(HttpSession session, Func<ValueTask> next);

}
