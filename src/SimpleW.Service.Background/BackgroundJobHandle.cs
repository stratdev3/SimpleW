namespace SimpleW.Service.Background {

    /// <summary>
    /// Lightweight handle returned when a background job is accepted.
    /// </summary>
    /// <param name="Id"></param>
    /// <param name="Name"></param>
    /// <param name="EnqueuedAtUtc"></param>
    public readonly record struct BackgroundJobHandle(Guid Id, string Name, DateTimeOffset EnqueuedAtUtc);

}
