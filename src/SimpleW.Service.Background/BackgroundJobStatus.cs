namespace SimpleW.Service.Background {

    /// <summary>
    /// Current state of a background job.
    /// </summary>
    public enum BackgroundJobStatus {

        /// <summary>
        /// The job is waiting in the queue.
        /// </summary>
        Queued,

        /// <summary>
        /// The job is currently running.
        /// </summary>
        Running,

        /// <summary>
        /// The job completed successfully.
        /// </summary>
        Succeeded,

        /// <summary>
        /// The job failed with an exception.
        /// </summary>
        Failed,

        /// <summary>
        /// The job was canceled.
        /// </summary>
        Canceled

    }

}
