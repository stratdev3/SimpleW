using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;


namespace SimpleW.Helper.Hosting {

    /// <summary>
    /// Host Builder
    /// </summary>
    public sealed class SimpleWHostApplicationBuilder {

        /// <summary>
        /// HostBuilder
        /// </summary>
        internal HostApplicationBuilder HostBuilder { get; }

        /// <summary>
        /// Services
        /// </summary>
        public IServiceCollection Services => HostBuilder.Services;

        /// <summary>
        /// Configuration
        /// </summary>
        public IConfiguration Configuration => HostBuilder.Configuration;

        /// <summary>
        /// ConfigureApp
        /// </summary>
        private Action<SimpleWServer>? _configureApp;

        /// <summary>
        /// ConfigureServer
        /// </summary>
        private Action<SimpleWSServerOptions>? _configureServer;

        /// <summary>
        /// URL override from code (UseUrls)
        /// </summary>
        private string? _overrideUrl;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="args"></param>
        internal SimpleWHostApplicationBuilder(string[] args) {
            HostBuilder = Host.CreateApplicationBuilder(args);
            Services.AddOptions<SimpleWHostOptions>().Bind(Configuration.GetSection("SimpleW"));
        }

        /// <summary>
        /// Override listen URL from code (priority over configuration).
        /// default: UseUrls("http://0.0.0.0:8080")
        /// </summary>
        public SimpleWHostApplicationBuilder UseUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) {
                throw new ArgumentException("url cannot be null/empty", nameof(url));
            }
            _overrideUrl = url;
            return this;
        }

        /// <summary>
        /// Configure SimpleW
        /// </summary>
        public SimpleWHostApplicationBuilder ConfigureSimpleW(Action<SimpleWServer> configureApp, Action<SimpleWSServerOptions>? configureServer = null) {
            _configureApp = configureApp ?? throw new ArgumentNullException(nameof(configureApp));
            _configureServer = configureServer;
            return this;
        }

        /// <summary>
        /// Build
        /// </summary>
        /// <returns></returns>
        public IHost Build() {
            // register the server singleton
            Services.AddSingleton(sp => {
                SimpleWHostOptions opt = sp.GetRequiredService<IOptions<SimpleWHostOptions>>().Value;

                string url = _overrideUrl ?? opt.Url;
                if (string.IsNullOrWhiteSpace(url)) {
                    throw new InvalidOperationException("SimpleW Url is empty. Set SimpleW:Url in config or call UseUrl().");
                }

                EndPoint endpoint = ParseEndPoint(url);
                SimpleWServer server = new(endpoint);

                if (_configureServer != null) {
                    server.Configure(_configureServer);
                }

                _configureApp?.Invoke(server);

                return server;
            });

            // register hosted service that starts/stops server
            Services.AddHostedService<SimpleWHostedService>();

            return HostBuilder.Build();
        }

        /// <summary>
        /// Convert Url to Endpoint
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        private static EndPoint ParseEndPoint(string url) {
            Uri u = new(url);
            if (!string.Equals(u.Scheme, "http", StringComparison.OrdinalIgnoreCase)) {
                throw new NotSupportedException($"Only http:// supported for now. Got: {u.Scheme}");
            }

            IPAddress ip = IPAddress.Parse(u.Host);
            return new IPEndPoint(ip, u.Port);
        }

    }

}

