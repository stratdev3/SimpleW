using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Background {

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
