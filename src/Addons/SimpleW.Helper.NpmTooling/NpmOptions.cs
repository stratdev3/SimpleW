using SimpleW.Observability;


namespace SimpleW.Helper.NpmTooling {

    /// <summary>
    /// Configuration of the npm development server and static SPA host.
    /// </summary>
    public sealed class NpmOptions {

        /// <summary>
        /// Frontend hosting mode. AutoDetect uses Production for published applications and Development for regular builds.
        /// </summary>
        public NpmEnvironment Environment { get; set; } = NpmEnvironment.AutoDetect;

        /// <summary>
        /// Client directory containing package.json, relative to AppContext.BaseDirectory or absolute.
        /// </summary>
        public string SourcePath { get; set; } = "../client";

        /// <summary>
        /// Compiled assets directory, relative to AppContext.BaseDirectory or absolute.
        /// </summary>
        public string ProductionPath { get; set; } = "wwwroot";

        /// <summary>
        /// API path relative to the active SimpleW origin, or a complete HTTP(S) URL used unchanged.
        /// </summary>
        public string ApiUrl { get; set; } = "/api";

        /// <summary>
        /// Allow the frontend origin on the API prefix in Development, without credentials.
        /// </summary>
        public bool EnableDevelopmentCors { get; set; } = true;

        /// <summary>
        /// Open the frontend URL when it becomes available.
        /// </summary>
        public bool OpenBrowser { get; set; } = true;

        /// <summary>
        /// Server lifecycle and supervision logger.
        /// </summary>
        public ILogger ServerLogger { get; set; } = new Logger("SimpleW.Helper.NpmTooling.Server");

        /// <summary>
        /// Captured npm stdout and stderr logger.
        /// </summary>
        public ILogger ClientLogger { get; set; } = new Logger("SimpleW.Helper.NpmTooling.Client");

        #region node

        /// <summary>
        /// Script name passed to npm run.
        /// </summary>
        public string Script { get; set; } = "dev";

        /// <summary>
        /// Arguments passed to the script after npm's -- separator.
        /// </summary>
        public List<string> ScriptArguments { get; } = new();

        /// <summary>
        /// Install missing direct dependencies before starting the development script.
        /// </summary>
        public bool InstallDependencies { get; set; } = true;

        /// <summary>
        /// Maximum time to discover the frontend URL from its output and receive a successful HTTP response.
        /// </summary>
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Maximum npm installation duration.
        /// </summary>
        public TimeSpan InstallTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Optional Node executable path.
        /// </summary>
        public string? NodePath { get; set; }

        /// <summary>
        /// Optional npm-cli.js path; npm is invoked with Node without a shell.
        /// </summary>
        public string? NpmCliPath { get; set; }

        #endregion node

    }

}
