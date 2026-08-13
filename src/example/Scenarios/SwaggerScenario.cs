using SimpleW;
using SimpleW.Helper.Swagger;


namespace example;

internal sealed class SwaggerScenario : IScenario {

    public string Name => "swagger";

    public string Description => "Small API with generated Swagger JSON and browser UI.";

    public string BrowserPath => "/admin/swagger";

    public string Usage => string.Empty;

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/", (HttpSession session) => session.Response.Redirect("/admin/swagger"));
        server.MapGet("/api/hello", (string? name = null) => new { message = $"Hello {name ?? "World"} !" });
        server.MapPost("/api/echo", (HttpSession session) => new { body = session.Request.BodyString });
        server.MapGet("/swagger.json", (HttpSession session) => Swagger.Json(session, ConfigureSwagger));
        server.MapGet("/admin/swagger", (HttpSession session) => Swagger.UI(session, ConfigureSwagger));

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

    private static void ConfigureSwagger(SwaggerOptions options) {
        options.Title = "SimpleW example API";
        options.Version = "v1";
        options.Description = "Routes exposed by the standalone Swagger scenario.";
        options.RouteFilter = route => route.Path.StartsWith("/api/", StringComparison.Ordinal);
    }

}
