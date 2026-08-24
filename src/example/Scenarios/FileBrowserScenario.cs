using SimpleW;
using SimpleW.Service.FileBrowser;


namespace example;

internal sealed class FileBrowserScenario : IScenario {

    public string Name => "filebrowser";

    public string Description => "Browse, upload and manage files through the FileBrowser web UI.";

    public string BrowserPath => "/files/";

    public string Usage => " [--directory path]";

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("directory");

        string? configuredDirectory = arguments.Get("directory");
        string browserDirectory = configuredDirectory == null
                                      ? throw new ArgumentException("You must provide a --directory argument !")
                                      : Path.GetFullPath(configuredDirectory);

        ExampleConsole.Info($"FileBrowser directory: '{browserDirectory}'.");
        ExampleConsole.Warning("Anonymous access is enabled for this local example.");

        SimpleWServer server = ExampleServer.Create(arguments);
        server.Configure(options => {
            options.MaxRequestBodySize = 32 * 1024 * 1024;
        });
        server.UseFileBrowserModule(options => {
            options.Path = browserDirectory;
            options.Prefix = "/files";
            options.AllowAnonymous = true;
            options.UploadChunkThresholdBytes = 16 * 1024 * 1024;
            options.UploadChunkBytes = 8 * 1024 * 1024;
        });

        await ExampleServer.RunAsync(server, "http", arguments, cancellationToken).ConfigureAwait(false);
    }

}
