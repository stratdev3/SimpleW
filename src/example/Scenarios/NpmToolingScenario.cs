using SimpleW;
using SimpleW.Helper.NpmTooling;


namespace example;

internal sealed class NpmToolingScenario : IScenario {

    public string Name => "npm";

    public string Description => "Start an existing Vite frontend with direct AJAX calls to SimpleW.";

    public string Usage => " --client <path>";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("client");

        string clientPath = Path.GetFullPath(arguments.Require("client"));
        if (!Directory.Exists(clientPath)) {
            throw new ExampleCliException($"Frontend directory '{clientPath}' does not exist.");
        }
        if (!File.Exists(Path.Combine(clientPath, "package.json"))) {
            throw new ExampleCliException($"Frontend directory '{clientPath}' must contain a package.json file.");
        }

        // The npm helper opens the frontend only after it is ready.
        bool openBrowser = arguments.BrowserEnabled;
        arguments.ConfigureBrowser(false, arguments.BrowserPath);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/api/hello", () => new {
            message = "Hello from SimpleW!",
            utc = DateTimeOffset.UtcNow
        });

        server.UseNpmToolingModule(options => {
            options.Environment = "Development";
            options.SourcePath = clientPath;
            options.DevelopmentUrl = "http://localhost:5173";
            options.ApiPrefix = "/api";
            options.LaunchDevelopmentServer = true;
            options.InstallDependencies = true;
            options.EnableDevelopmentCors = true;
            options.OpenBrowser = openBrowser;
        });

        ExampleConsole.Info($"Frontend sources: {clientPath}");
        ExampleConsole.Info("Frontend: http://localhost:5173 (available after npm startup).");
        ExampleConsole.Info("The helper passes the actual API address through VITE_SIMPLEW_API_URL.");
        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
