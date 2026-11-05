using SimpleW.Observability;


namespace SimpleW.Helper.NpmTooling {

    /// <summary>
    /// Configuration of the npm development server and static SPA host.
    /// </summary>
    public sealed class NpmOptions {

        /// <summary>
        /// Explicit environment, otherwise DOTNET_ENVIRONMENT or Production.
        /// </summary>
        public string? Environment { get; set; }

        /// <summary>
        /// Frontend configuration.
        /// </summary>
        public NpmSpaOptions Spa { get; } = new();

        /// <summary>
        /// Server lifecycle and supervision logger.
        /// </summary>
        public ILogger ServerLogger { get; set; } = new Logger("SimpleW.Helper.NpmTooling.Server");

        /// <summary>
        /// Captured npm stdout and stderr logger.
        /// </summary>
        public ILogger ClientLogger { get; set; } = new Logger("SimpleW.Helper.NpmTooling.Client");

    }

    /// <summary>
    /// Development and production frontend options.
    /// </summary>
    public sealed class NpmSpaOptions {

        /// <summary>
        /// Client directory containing package.json, relative to AppContext.BaseDirectory or absolute.
        /// </summary>
        public string SourcePath { get; set; } = "../client";

        /// <summary>
        /// Compiled assets directory, relative to AppContext.BaseDirectory or absolute.
        /// </summary>
        public string ProductionPath { get; set; } = "wwwroot";

        /// <summary>
        /// Local frontend HTTP(S) origin, also used for the browser and CORS.
        /// </summary>
        public string DevelopmentUrl { get; set; } = "http://localhost:5173";

        /// <summary>
        /// Override for the backend origin; otherwise derived from the actual listener.
        /// </summary>
        public string? BackendUrl { get; set; }

        /// <summary>
        /// Script name passed to npm run.
        /// </summary>
        public string Script { get; set; } = "dev";

        /// <summary>
        /// Arguments passed to the script after npm's -- separator.
        /// </summary>
        public List<string> ScriptArguments { get; } = new();

        /// <summary>
        /// False waits for an externally managed frontend without starting npm.
        /// </summary>
        public bool LaunchDevelopmentServer { get; set; } = true;

        /// <summary>
        /// Allow the frontend origin on the API prefix in Development, without credentials.
        /// </summary>
        public bool EnableDevelopmentCors { get; set; } = true;

        /// <summary>
        /// Install missing direct dependencies before starting the development script.
        /// </summary>
        public bool InstallDependencies { get; set; } = true;

        /// <summary>
        /// Open the frontend URL when it becomes available.
        /// </summary>
        public bool OpenBrowser { get; set; } = true;

        /// <summary>
        /// Maximum time to wait for a successful frontend HTTP response.
        /// </summary>
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Maximum npm installation duration.
        /// </summary>
        public TimeSpan InstallTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// REST API prefix appended to the backend origin and excluded from SPA fallback.
        /// </summary>
        public string ApiPrefix { get; set; } = "/api";

        /// <summary>
        /// Optional Node executable path.
        /// </summary>
        public string? NodePath { get; set; }

        /// <summary>
        /// Optional npm-cli.js path; npm is invoked with Node without a shell.
        /// </summary>
        public string? NpmCliPath { get; set; }

    }

}
