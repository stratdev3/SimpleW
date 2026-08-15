namespace SimpleW.Service.Background {

    /// <summary>
    /// Options for one fixed-interval schedule.
    /// </summary>
    public sealed class BackgroundIntervalOptions {

        #region schedule policy

        /// <summary>
        /// Enable or disable this schedule.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Allows a new occurrence to be queued while the previous occurrence is queued, running, or retrying.
        /// </summary>
        public bool AllowConcurrentExecutions { get; set; }

        /// <summary>
        /// Execution options applied to every occurrence.
        /// </summary>
        public BackgroundJobOptions JobOptions { get; }

        #endregion schedule policy

        #region construction

        /// <summary>
        /// Constructor
        /// </summary>
        public BackgroundIntervalOptions() : this(new BackgroundJobOptions()) { }

        /// <summary>
        /// Creates interval options around a cloned set of global job defaults.
        /// </summary>
        /// <param name="jobOptions"></param>
        internal BackgroundIntervalOptions(BackgroundJobOptions jobOptions) {
            JobOptions = jobOptions ?? throw new ArgumentNullException(nameof(jobOptions));
        }

        #endregion construction

    }

}
