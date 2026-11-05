using SimpleW.Helper.NpmTooling.Internal;
using SimpleW.Modules;


namespace SimpleW.Helper.NpmTooling {

    /// <summary>
    /// Connects npm supervision to the existing SimpleW lifecycle.
    /// </summary>
    /// <param name="options">Environment, frontend settings and loggers used when installing the module.</param>
    internal sealed class NpmToolingModule(NpmOptions options) : IHttpModule {

        /// <summary>
        /// Installs production static hosting or connects development supervision and CORS to the server lifecycle.
        /// </summary>
        /// <param name="server">Server configured before its listener starts.</param>
        public void Install(SimpleWServer server) {

            string environment = options.Environment ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)) {
                new SpaFiles(options).Install(server);
                return;
            }

            DevelopmentServer development = new(server, options);

            // cors
            if (options.EnableDevelopmentCors) {
                server.UseCorsModule(cors => {
                    cors.Prefix = options.ApiPrefix;
                    cors.AllowedOrigins = [ new Uri(options.DevelopmentUrl).GetLeftPart(UriPartial.Authority) ];
                    cors.AllowCredentials = false;
                    cors.AllowedHeaders = "Content-Type, Authorization";
                });
            }

            // track server state
            server.OnStateChanged(async (_, state) => {
                if (state == SimpleWServerState.Started) {
                    // installation and HTTP polling never block the serialized server lifecycle.
                    development.Start();
                }
                else if (state is SimpleWServerState.Stopping or SimpleWServerState.Stopped or SimpleWServerState.Faulted) {
                    await development.StopAsync().ConfigureAwait(false);
                }
            });
        }

    }

}
