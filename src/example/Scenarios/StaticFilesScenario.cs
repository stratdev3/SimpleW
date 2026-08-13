using SimpleW;
using SimpleW.Modules;


namespace example;

internal sealed class StaticFilesScenario : IScenario {

    private const string IndexHtml = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>SimpleW static files</title>
          <style>
            body { max-width: 48rem; margin: 4rem auto; padding: 0 1rem; font: 18px/1.5 system-ui, sans-serif; }
            code { background: #eee; padding: .15rem .35rem; }
          </style>
        </head>
        <body>
          <h1>Static files work</h1>
          <p>This page is served by <code>UseStaticFilesModule</code>.</p>
        </body>
        </html>
        """;

    public string Name => "static";

    public string Description => "Serve the bundled static site with cache and directory listing.";

    public string Usage => string.Empty;

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        string staticPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Static");
        await ExampleRuntimeFile.MaterializeAsync(
            Path.Combine(staticPath, "index.html"),
            IndexHtml + Environment.NewLine,
            cancellationToken
        ).ConfigureAwait(false);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseStaticFilesModule(options => {
            options.Path = staticPath;
            options.Prefix = "/";
            options.DefaultDocument = "index.html";
            options.AutoIndex = true;
            options.CacheTimeout = TimeSpan.FromMinutes(10);
        });

        await ExampleServer.RunAsync(server, "http", arguments, cancellationToken).ConfigureAwait(false);
    }

}
