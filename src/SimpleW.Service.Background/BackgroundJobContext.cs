namespace SimpleW.Service.Background {

    /// <summary>
    /// Context passed to a running background job.
    /// </summary>
    public sealed class BackgroundJobContext {

        /// <summary>
        /// Job identifier.
        /// </summary>
        public Guid JobId { get; }

        /// <summary>
        /// Job name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Optional origin of the job, for example "handler" or "cron".
        /// </summary>
        public string? Source { get; }

        /// <summary>
        /// UTC date at which the job was queued.
        /// </summary>
        public DateTimeOffset EnqueuedAtUtc { get; }

        /// <summary>
        /// Cancellation token signaled when the background service stops.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        private readonly Action<double?, string?> _reportProgress;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="jobId"></param>
        /// <param name="name"></param>
        /// <param name="source"></param>
        /// <param name="enqueuedAtUtc"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="reportProgress"></param>
        /// <exception cref="ArgumentNullException"></exception>
        internal BackgroundJobContext(Guid jobId, string name, string? source, DateTimeOffset enqueuedAtUtc, CancellationToken cancellationToken, Action<double?, string?> reportProgress) {
            JobId = jobId;
            Name = name;
            Source = source;
            EnqueuedAtUtc = enqueuedAtUtc;
            CancellationToken = cancellationToken;
            _reportProgress = reportProgress ?? throw new ArgumentNullException(nameof(reportProgress));
        }

        /// <summary>
        /// Throws when the background service is stopping.
        /// </summary>
        public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

        /// <summary>
        /// Reports the latest known progress for this job.
        /// </summary>
        /// <param name="percent">Optional progress percentage. Values are normalized to the 0..100 range.</param>
        /// <param name="message">Optional progress message. Null keeps the previous message.</param>
        public void ReportProgress(double? percent = null, string? message = null) => _reportProgress(percent, message);

        /// <summary>
        /// Reports the latest known progress message for this job.
        /// </summary>
        /// <param name="message"></param>
        public void ReportProgress(string message) => ReportProgress(percent: null, message);

    }

}
