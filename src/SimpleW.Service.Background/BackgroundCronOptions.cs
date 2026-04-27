using Cronos;


namespace SimpleW.Service.Background {

    /// <summary>
    /// Options for one scheduled cron job.
    /// </summary>
    public sealed class BackgroundCronOptions {

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
        /// Allows a new occurrence to be queued while the previous one is queued or running.
        /// </summary>
        public bool AllowConcurrentExecutions { get; set; } = false;

    }

}
