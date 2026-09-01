using Cronos;


namespace SimpleW.Service.Background {

    /// <summary>
    /// Options for one scheduled cron job.
    /// </summary>
    public sealed class BackgroundCronOptions {

        #region schedule policy

        /// <summary>
        /// Enable or disable this schedule.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Cron expression format.
        /// </summary>
        public CronFormat CronFormat { get; set; } = CronFormat.Standard;

        /// <summary>
        /// Time zone used to calculate next occurrences.
        /// </summary>
        public TimeZoneInfo? TimeZone { get; set; }

        /// <summary>
        /// Allows a new occurrence to be queued while the previous one is queued, running, or retrying.
        /// </summary>
        public bool AllowConcurrentExecutions { get; set; } = false;

        /// <summary>
        /// Execution options applied to every occurrence.
        /// </summary>
        public BackgroundJobOptions JobOptions { get; }

        #endregion schedule policy

        #region construction

        /// <summary>
        /// Constructor
        /// </summary>
        public BackgroundCronOptions() : this(new BackgroundJobOptions()) { }

        /// <summary>
        /// Creates cron options around a cloned set of global job defaults.
        /// </summary>
        /// <param name="jobOptions"></param>
        internal BackgroundCronOptions(BackgroundJobOptions jobOptions) {
            JobOptions = jobOptions ?? throw new ArgumentNullException(nameof(jobOptions));
        }

        #endregion construction

    }

}
