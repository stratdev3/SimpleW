namespace SimpleW.Service.Background {

    /// <summary>
    /// Immutable view of a background job state.
    /// </summary>
    /// <param name="Id"></param>
    /// <param name="Name"></param>
    /// <param name="Source"></param>
    /// <param name="Status"></param>
    /// <param name="EnqueuedAtUtc"></param>
    /// <param name="StartedAtUtc"></param>
    /// <param name="FinishedAtUtc"></param>
    /// <param name="Error"></param>
    /// <param name="Progress"></param>
    /// <param name="ProgressMessage"></param>
    /// <param name="UpdatedAtUtc"></param>
    public sealed record BackgroundJobSnapshot(
        Guid Id,
        string Name,
        string? Source,
        BackgroundJobStatus Status,
        DateTimeOffset EnqueuedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? FinishedAtUtc,
        string? Error,
        double? Progress,
        string? ProgressMessage,
        DateTimeOffset? UpdatedAtUtc
    ) {

        /// <summary>
        /// Job duration when the job has started.
        /// </summary>
        public TimeSpan? Duration {
            get {
                if (StartedAtUtc == null) {
                    return null;
                }
                return (FinishedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc.Value;
            }
        }

    }

}
