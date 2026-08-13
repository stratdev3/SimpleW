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

        #region scheduling and retry details

        /// <summary>
        /// Initial date at which a delayed job is due.
        /// </summary>
        public DateTimeOffset? ScheduledAtUtc { get; init; }

        /// <summary>
        /// Date at which the next retry is due.
        /// </summary>
        public DateTimeOffset? NextAttemptAtUtc { get; init; }

        /// <summary>
        /// Current or last completed attempt number, starting at one.
        /// </summary>
        public int Attempt { get; init; }

        /// <summary>
        /// Maximum number of attempts, including the first execution.
        /// </summary>
        public int MaxAttempts { get; init; } = 1;

        /// <summary>
        /// Error produced by the latest failed attempt.
        /// </summary>
        public string? LastError { get; init; }

        /// <summary>
        /// Timeout applied independently to every attempt.
        /// </summary>
        public TimeSpan? Timeout { get; init; }

        #endregion scheduling and retry details

        #region computed values

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

        #endregion computed values

    }

}
