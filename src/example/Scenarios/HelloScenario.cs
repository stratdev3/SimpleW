using SimpleW;


namespace example;

internal sealed class HelloScenario : IScenario {

    public string Name => "hello";

    public string Description => "Minimal text, empty-task and JSON routes.";

    public string Usage => string.Empty;

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        SimpleWServer server = ExampleServer.Create(arguments);

        server.MapGet("/", () => "Hello World !");
        server.MapGet("/api/test/hello", () => new { message = "Hello World !" });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
