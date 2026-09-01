using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace SimpleW.Helper.Hosting {

    /// <summary>
    /// Host Service
    /// </summary>
    internal sealed class SimpleWHostedService : IHostedService {

        /// <summary>
        /// The underlying SimpleWServer instance
        /// </summary>
        private readonly SimpleWServer _server;

        /// <summary>
        /// IHostApplicationLifetime
        /// </summary>
        private readonly IHostApplicationLifetime _lifetime;

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger<SimpleWHostedService> _logger;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="server"></param>
        /// <param name="lifetime"></param>
        /// <param name="logger"></param>
        public SimpleWHostedService(SimpleWServer server, IHostApplicationLifetime lifetime, ILogger<SimpleWHostedService> logger) {
            _server = server;
            _lifetime = lifetime;
            _logger = logger;
        }

        /// <summary>
        /// StartAsync
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Starting SimpleW on {EndPoint}", _server.EndPoint);
            await _server.StartAsync(_lifetime.ApplicationStopping);
        }

        /// <summary>
        /// StopAsync
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Stopping SimpleW...");
            await _server.StopAsync();
        }

    }

}

