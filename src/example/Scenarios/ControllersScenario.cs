using SimpleW;
using SimpleW.Helper.Razor;


namespace example;

internal sealed class ControllersScenario : IScenario {

    public string Name => "controllers";

    public string Description => "Attribute-routed classic and Razor controllers.";

    public string Usage => string.Empty;

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        await RazorScenarioAssets.MaterializeAsync(cancellationToken).ConfigureAwait(false);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseRazorModule(options => {
            options.ViewsPath = RazorScenarioAssets.ViewsPath;
        });
        server.MapGet("/", () => new {
            api = "/api/demo/hello?name=SimpleW",
            view = "/pages/home"
        });
        server.MapController<ExampleApiController>("/api");
        server.MapController<ExampleRazorController>("/pages");

        await ExampleServer.RunAsync(server, "http", arguments, cancellationToken).ConfigureAwait(false);
    }

}

[Route("/demo")]
public sealed class ExampleApiController : Controller {

    [Route("GET", "/hello", Description = "Return a greeting from a classic controller.")]
    public object Hello(string? name = null) {
        return new { message = $"Hello {name ?? "World"} !", controller = GetType().Name };
    }
}

public sealed class ExampleRazorController : RazorController {

    [Route("GET", "/home", Description = "Render a greeting from a Razor controller.")]
    public object Home() {
        return View("Home", new { Title = "SimpleW controller", Name = "controller" })
               .WithViewBag(viewBag => {
                   viewBag.Footer = "Rendered from ExampleRazorController";
               });
    }
}
