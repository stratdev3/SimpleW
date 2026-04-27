using System.Runtime.CompilerServices;
using Cronos;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Background {

    /// <summary>
    /// BackgroundModuleExtension
    /// </summary>
    public static class BackgroundModuleExtension {

        private static readonly ConditionalWeakTable<SimpleWServer, IBackgroundService> _services = new();

        /// <summary>
        /// Enables the in-process background service.
        /// </summary>
        /// <example>
        /// server.UseBackgroundModule(options => {
        ///     options.WorkerCount = 2;
        ///     options.JobStore = new MemoryBackgroundJobStore();
        /// 
        ///     options.Schedule("cleanup", "0 2 * * *", async ctx => {
        ///         await CleanupAsync(ctx.CancellationToken);
        ///     });
        /// });
        /// 
        /// server.MapPost("/import", (HttpSession session) => {
        ///     string payload = session.Request.BodyString;
        /// 
        ///     BackgroundJobHandle job = session.GetBackgroundService().Enqueue("import", async ctx => {
        ///         ctx.ReportProgress(0, "Starting import");
        ///         await ImportAsync(payload, ctx.CancellationToken);
        ///         ctx.ReportProgress(100, "Done");
        ///     });
        /// 
        ///     return session.Response.Status(202).Json(new {
        ///         accepted = true,
        ///         jobId = job.Id
        ///     });
        /// });
        /// </example>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseBackgroundModule(this SimpleWServer server, Action<BackgroundOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            BackgroundOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new BackgroundModule(options));
            return server;
        }

        /// <summary>
        /// Gets the background service attached to a server.
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public static IBackgroundService GetBackgroundService(this SimpleWServer server) {
            ArgumentNullException.ThrowIfNull(server);

            if (_services.TryGetValue(server, out IBackgroundService? service)) {
                return service;
            }

            throw new InvalidOperationException("Background module is not installed. Call UseBackgroundModule(...) before using background jobs.");
        }

        /// <summary>
        /// Gets the background service attached to the current server.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public static IBackgroundService GetBackgroundService(this HttpSession session) {
            ArgumentNullException.ThrowIfNull(session);
            return session.Server.GetBackgroundService();
        }

        /// <summary>
        /// Gets the background service attached to the current server.
        /// </summary>
        /// <param name="controller"></param>
        /// <returns></returns>
        public static IBackgroundService GetBackgroundService(this Controller controller) {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.Session.GetBackgroundService();
        }

        internal static void Register(SimpleWServer server, IBackgroundService service) {
            try {
                _services.Add(server, service);
            }
            catch (ArgumentException ex) {
                throw new InvalidOperationException("Background module is already installed for this server.", ex);
            }
        }

    }

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

    /// <summary>
    /// Background Module
    /// </summary>
    internal sealed class BackgroundModule : IHttpModule {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger _log = new Logger<BackgroundModule>();

        /// <summary>
        /// Background Service
        /// </summary>
        private readonly BackgroundService _service;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public BackgroundModule(BackgroundOptions options) {
            _service = new BackgroundService(options);
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        public void Install(SimpleWServer server) {
            if (server.IsStarted) {
                InvalidOperationException ex = new("BackgroundModule must be installed before server start.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }

            _log.Info("installing...");

            BackgroundModuleExtension.Register(server, _service);
            _service.AttachServer(server);

            server.OnStarted(_ => {
                _service.Start();
            });

            server.OnStopped(async _ => {
                await _service.StopAsync().ConfigureAwait(false);
            });

            _log.Info("installed");
        }

    }

}
