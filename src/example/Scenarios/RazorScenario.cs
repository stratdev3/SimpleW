using SimpleW;
using SimpleW.Helper.Razor;


namespace example;

internal sealed class RazorScenario : IScenario {

    public string Name => "razor";

    public string Description => "Render a bundled Razor view with a model and ViewBag.";

    public string Usage => string.Empty;

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        await RazorScenarioAssets.MaterializeAsync(cancellationToken).ConfigureAwait(false);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseRazorModule(options => {
            options.ViewsPath = RazorScenarioAssets.ViewsPath;
        });
        server.MapGet("/", () => {
            return RazorResults.View("Home", new { Title = "SimpleW Razor", Name = "developer" })
                               .WithViewBag(viewBag => {
                                   viewBag.Footer = "Rendered by SimpleW.Helper.Razor";
                               });
        });

        await ExampleServer.RunAsync(server, "http", arguments, cancellationToken).ConfigureAwait(false);
    }
}

internal static class RazorScenarioAssets {

    private const string HomeView = """
        @model dynamic
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>@Model.Title</title>
        </head>
        <body>
          <h1>Hello @Model.Name!</h1>
          <p>@ViewBag.Footer</p>
        </body>
        </html>
        """;

    public static string ViewsPath => Path.Combine(AppContext.BaseDirectory, "Views");

    public static Task MaterializeAsync(CancellationToken cancellationToken) {
        return ExampleRuntimeFile.MaterializeAsync(
            Path.Combine(ViewsPath, "Home.cshtml"),
            HomeView + Environment.NewLine,
            cancellationToken
        );
    }

}
