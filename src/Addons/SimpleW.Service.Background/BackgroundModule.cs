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

            server.OnStateChanged(async (_, state) => {
                if (state == SimpleWServerState.Started) {
                    _service.Start();
                }
                else if (state == SimpleWServerState.Stopped) {
                    try {
                        await _service.StopAsync().ConfigureAwait(false);
                    }
                    finally {
                        _service.ResetTelemetry();
                    }
                }
            });

            _log.Info("installed");
        }

    }

}
