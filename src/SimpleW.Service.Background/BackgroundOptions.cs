using Cronos;
using SimpleW.Observability;


namespace SimpleW.Service.Background {

    /// <summary>
    /// Background module options.
    /// </summary>
    public sealed class BackgroundOptions {

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

        internal List<BackgroundCronRegistration> Schedules { get; } = new();

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

            BackgroundCronOptions options = new();
            configure?.Invoke(options);

            Schedules.Add(new BackgroundCronRegistration(name, cronExpression, work, options));
            return this;
        }

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

            foreach (BackgroundCronRegistration schedule in Schedules) {
                try {
                    _ = schedule.GetExpression();
                }
                catch (Exception ex) {
                    ArgumentException wrapped = new($"Invalid cron expression for schedule '{schedule.Name}'.", ex);
                    _log.Fatal(wrapped.Message, wrapped);
                    throw wrapped;
                }
            }

            return this;
        }

        /// <summary>
        /// BackgroundCronRegistration
        /// </summary>
        internal sealed class BackgroundCronRegistration {

            public string Name { get; }
            public string CronExpression { get; }
            public Func<BackgroundJobContext, Task> Work { get; }
            public BackgroundCronOptions Options { get; }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="name"></param>
            /// <param name="cronExpression"></param>
            /// <param name="work"></param>
            /// <param name="options"></param>
            /// <exception cref="ArgumentException"></exception>
            /// <exception cref="ArgumentNullException"></exception>
            public BackgroundCronRegistration(string name, string cronExpression, Func<BackgroundJobContext, Task> work, BackgroundCronOptions options) {
                if (string.IsNullOrWhiteSpace(name)) {
                    throw new ArgumentException("Schedule name cannot be empty.", nameof(name));
                }
                if (string.IsNullOrWhiteSpace(cronExpression)) {
                    throw new ArgumentException("Cron expression cannot be empty.", nameof(cronExpression));
                }

                Name = name.Trim();
                CronExpression = cronExpression.Trim();
                Work = work ?? throw new ArgumentNullException(nameof(work));
                Options = options ?? throw new ArgumentNullException(nameof(options));
            }

            public CronExpression GetExpression() => Cronos.CronExpression.Parse(CronExpression, Options.CronFormat);

        }

    }

}
