using SimpleW;
using SimpleW.Helper.NpmTooling;


namespace example;

internal sealed class NpmToolingScenario : IScenario {

    public string Name => "npm";

    public string Description => "Start an existing Vite frontend with direct AJAX calls to SimpleW.";

    public string Usage => "";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        // The npm helper opens the frontend only after it is ready.
        bool openBrowser = arguments.BrowserEnabled;
        arguments.ConfigureBrowser(false, arguments.BrowserPath);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/api/hello", () => new {
            message = "Hello from SimpleW!",
            utc = DateTimeOffset.UtcNow
        });

        server.UseNpmToolingModule(options => {
            options.OpenBrowser = openBrowser;
        });

        ExampleConsole.Info("The frontend address will be detected automatically from its startup output.");
        ExampleConsole.Info("The helper passes the actual API address through SIMPLEW_API_URL and VITE_SIMPLEW_API_URL.");
        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
