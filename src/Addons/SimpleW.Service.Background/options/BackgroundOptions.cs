using Cronos;
using SimpleW.Observability;


namespace SimpleW.Service.Background {

    /// <summary>
    /// Background module options.
    /// </summary>
    public sealed class BackgroundOptions {

        #region configuration

        /// <summary>
        /// Logger used to report invalid schedule configuration.
        /// </summary>
        private static readonly ILogger _log = new Logger<BackgroundOptions>();

        /// <summary>
        /// Number of worker loops processing queued jobs.
        /// </summary>
        public int WorkerCount { get; set; } = 1;

        /// <summary>
        /// Maximum number of queued jobs.
        /// </summary>
        public int Capacity { get; set; } = 1024;

        /// <summary>
        /// Number of completed jobs kept in memory.
        /// </summary>
        public int CompletedJobRetention { get; set; } = 1000;

        /// <summary>
        /// Maximum time to wait for worker and cron loops during shutdown.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Default time zone for cron schedules.
        /// </summary>
        public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

        /// <summary>
        /// Store used for job snapshots. Defaults to an in-memory store.
        /// </summary>
        public IBackgroundJobStore? JobStore { get; set; }

        /// <summary>
        /// Enable module telemetry. The underlying SimpleWServer telemetry must also be enabled.
        /// </summary>
        public bool EnableTelemetry { get; set; }

        /// <summary>
        /// Default execution options copied by every background job.
        /// </summary>
        public BackgroundJobOptions DefaultJobOptions { get; } = new();

        /// <summary>
        /// Cron registrations materialized when the service starts.
        /// </summary>
        internal List<BackgroundCronRegistration> Schedules { get; } = new();

        /// <summary>
        /// Fixed-interval registrations materialized when the service starts.
        /// </summary>
        internal List<BackgroundIntervalRegistration> Intervals { get; } = new();

        #endregion configuration

        #region schedule registration

        /// <summary>
        /// Adds a cron schedule.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="cronExpression"></param>
        /// <param name="work"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public BackgroundOptions Schedule(string name, string cronExpression, Func<BackgroundJobContext, Task> work, Action<BackgroundCronOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(work);

            // Defer option cloning until validation so global defaults are fully configured first.
            Schedules.Add(new BackgroundCronRegistration(name, cronExpression, work, configure));
            return this;
        }

        /// <summary>
        /// Adds a fixed-interval schedule. The first occurrence runs after one complete interval.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="interval"></param>
        /// <param name="work"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public BackgroundOptions ScheduleEvery(string name, TimeSpan interval, Func<BackgroundJobContext, Task> work, Action<BackgroundIntervalOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(work);

            Intervals.Add(new BackgroundIntervalRegistration(name, interval, work, configure));
            return this;
        }

        #endregion schedule registration

        #region validation

        /// <summary>
        /// Normalizes invalid scalar values and materializes every schedule's effective job options.
        /// </summary>
        /// <returns></returns>
        internal BackgroundOptions ValidateAndNormalize() {
            if (WorkerCount <= 0) {
                WorkerCount = 1;
            }
            if (Capacity <= 0) {
                Capacity = 1024;
            }
            if (CompletedJobRetention < 0) {
                CompletedJobRetention = 0;
            }
            if (ShutdownTimeout <= TimeSpan.Zero) {
                ShutdownTimeout = TimeSpan.FromSeconds(30);
            }
            TimeZone ??= TimeZoneInfo.Utc;
            DefaultJobOptions.ValidateAndNormalize();

            // Parsing during module installation fails fast before the server starts.
            foreach (BackgroundCronRegistration schedule in Schedules) {
                schedule.Configure(DefaultJobOptions);
                try {
                    _ = schedule.GetExpression();
                }
                catch (Exception ex) {
                    ArgumentException wrapped = new($"Invalid cron expression for schedule '{schedule.Name}'.", ex);
                    _log.Fatal(wrapped.Message, wrapped);
                    throw wrapped;
                }
            }

            foreach (BackgroundIntervalRegistration schedule in Intervals) {
                schedule.Configure(DefaultJobOptions);
            }

            return this;
        }

        #endregion validation

        #region cron registration

        /// <summary>
        /// BackgroundCronRegistration
        /// </summary>
        internal sealed class BackgroundCronRegistration {

            /// <summary>Human-readable schedule name.</summary>
            public string Name { get; }
            /// <summary>Normalized cron expression.</summary>
            public string CronExpression { get; }
            /// <summary>Delegate enqueued for each occurrence.</summary>
            public Func<BackgroundJobContext, Task> Work { get; }
            /// <summary>Effective options after global defaults and local configuration are applied.</summary>
            public BackgroundCronOptions Options { get; private set; } = null!;

            /// <summary>Deferred local configuration callback.</summary>
            private readonly Action<BackgroundCronOptions>? _configure;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="name"></param>
            /// <param name="cronExpression"></param>
            /// <param name="work"></param>
            /// <param name="configure"></param>
            /// <exception cref="ArgumentException"></exception>
            /// <exception cref="ArgumentNullException"></exception>
            public BackgroundCronRegistration(string name, string cronExpression, Func<BackgroundJobContext, Task> work, Action<BackgroundCronOptions>? configure) {
                if (string.IsNullOrWhiteSpace(name)) {
                    throw new ArgumentException("Schedule name cannot be empty.", nameof(name));
                }
                if (string.IsNullOrWhiteSpace(cronExpression)) {
                    throw new ArgumentException("Cron expression cannot be empty.", nameof(cronExpression));
                }

                Name = name.Trim();
                CronExpression = cronExpression.Trim();
                Work = work ?? throw new ArgumentNullException(nameof(work));
                _configure = configure;
            }

            /// <summary>
            /// Clones global job defaults and applies cron-specific configuration.
            /// </summary>
            /// <param name="defaultJobOptions"></param>
            public void Configure(BackgroundJobOptions defaultJobOptions) {
                Options = new BackgroundCronOptions(defaultJobOptions.Clone());
                _configure?.Invoke(Options);
                Options.JobOptions.ValidateAndNormalize();
            }

            /// <summary>
            /// Parses the configured expression using its selected cron format.
            /// </summary>
            /// <returns></returns>
            public CronExpression GetExpression() => Cronos.CronExpression.Parse(CronExpression, Options.CronFormat);

        }

        #endregion cron registration

        #region interval registration

        /// <summary>
        /// BackgroundIntervalRegistration
        /// </summary>
        internal sealed class BackgroundIntervalRegistration {

            /// <summary>Human-readable schedule name.</summary>
            public string Name { get; }
            /// <summary>Fixed duration between planned ticks.</summary>
            public TimeSpan Interval { get; }
            /// <summary>Delegate enqueued for each occurrence.</summary>
            public Func<BackgroundJobContext, Task> Work { get; }
            /// <summary>Effective options after global defaults and local configuration are applied.</summary>
            public BackgroundIntervalOptions Options { get; private set; } = null!;

            /// <summary>Deferred local configuration callback.</summary>
            private readonly Action<BackgroundIntervalOptions>? _configure;

            /// <summary>
            /// Creates and validates one fixed-interval registration.
            /// </summary>
            /// <param name="name"></param>
            /// <param name="interval"></param>
            /// <param name="work"></param>
            /// <param name="configure"></param>
            public BackgroundIntervalRegistration(string name, TimeSpan interval, Func<BackgroundJobContext, Task> work, Action<BackgroundIntervalOptions>? configure) {
                if (string.IsNullOrWhiteSpace(name)) {
                    throw new ArgumentException("Schedule name cannot be empty.", nameof(name));
                }
                if (interval <= TimeSpan.Zero) {
                    throw new ArgumentOutOfRangeException(nameof(interval), "Schedule interval must be greater than zero.");
                }

                Name = name.Trim();
                Interval = interval;
                Work = work ?? throw new ArgumentNullException(nameof(work));
                _configure = configure;
            }

            /// <summary>
            /// Clones global job defaults and applies interval-specific configuration.
            /// </summary>
            /// <param name="defaultJobOptions"></param>
            public void Configure(BackgroundJobOptions defaultJobOptions) {
                Options = new BackgroundIntervalOptions(defaultJobOptions.Clone());
                _configure?.Invoke(Options);
                Options.JobOptions.ValidateAndNormalize();
            }

        }

        #endregion interval registration

    }

}
