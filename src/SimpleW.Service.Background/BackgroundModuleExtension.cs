using SimpleW.Modules;


namespace SimpleW.Service.Background {

    /// <summary>
    /// BackgroundModuleExtension
    /// </summary>
    public static class BackgroundModuleExtension {

        #region instance registry

        /// <summary>
        /// Associates one background service instance with each SimpleW server without extending server lifetime.
        /// </summary>
        private static readonly ModuleInstanceRegistry<IBackgroundService> _instances = new();

        #endregion instance registry

        #region module registration

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

        #endregion module registration

        #region service resolution

        /// <summary>
        /// Gets the background service attached to a server.
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public static IBackgroundService GetBackgroundService(this SimpleWServer server) {
            ArgumentNullException.ThrowIfNull(server);

            if (_instances.TryGet(server, out IBackgroundService? instance) && instance != null) {
                return instance;
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

        #endregion service resolution

        #region internal registration

        /// <summary>
        /// Register the background service attached to the current server.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="instance"></param>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void Register(SimpleWServer server, IBackgroundService instance) {
            // Installing the module twice would otherwise create competing workers for the same server.
            if (!_instances.TryAdd(server, instance)) {
                throw new InvalidOperationException("Background module is already installed for this server.");
            }
        }

        #endregion internal registration

    }

}
