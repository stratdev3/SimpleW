namespace SimpleW.Service.Background {

    /// <summary>
    /// Execution options applied to one background job.
    /// </summary>
    public sealed class BackgroundJobOptions {

        #region execution policy

        /// <summary>
        /// Maximum duration of one attempt. Null disables the timeout.
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Number of additional attempts after the first failure.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Delay before the first retry.
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Multiplier applied to the delay after each failed attempt.
        /// </summary>
        public double RetryBackoffFactor { get; set; } = 2d;

        /// <summary>
        /// Maximum delay between attempts.
        /// </summary>
        public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Allows timed out attempts to be retried.
        /// </summary>
        public bool RetryOnTimeout { get; set; } = true;

        #endregion execution policy

        #region cloning and validation

        /// <summary>
        /// Creates an independent copy so per-job configuration cannot mutate global defaults.
        /// </summary>
        /// <returns></returns>
        internal BackgroundJobOptions Clone() {
            return new BackgroundJobOptions {
                Timeout = Timeout,
                RetryCount = RetryCount,
                RetryDelay = RetryDelay,
                RetryBackoffFactor = RetryBackoffFactor,
                RetryMaxDelay = RetryMaxDelay,
                RetryOnTimeout = RetryOnTimeout
            };
        }

        /// <summary>
        /// Converts invalid values to safe runtime defaults and returns this instance.
        /// </summary>
        /// <returns></returns>
        internal BackgroundJobOptions ValidateAndNormalize() {
            // A null timeout is the canonical representation of an unlimited attempt.
            if (Timeout <= TimeSpan.Zero) {
                Timeout = null;
            }

            // Retry values are made monotonic so the delay calculator can remain branch-free.
            if (RetryCount < 0) {
                RetryCount = 0;
            }
            if (RetryDelay < TimeSpan.Zero) {
                RetryDelay = TimeSpan.Zero;
            }
            if (double.IsNaN(RetryBackoffFactor) || double.IsInfinity(RetryBackoffFactor) || RetryBackoffFactor < 1d) {
                RetryBackoffFactor = 1d;
            }
            if (RetryMaxDelay < RetryDelay) {
                RetryMaxDelay = RetryDelay;
            }

            return this;
        }

        #endregion cloning and validation

    }

}
