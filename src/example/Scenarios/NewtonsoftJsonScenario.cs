using SimpleW;
using SimpleW.JsonEngine.Newtonsoft;


namespace example;

internal sealed class NewtonsoftJsonScenario : IScenario {

    public string Name => "json-newtonsoft";

    public string Description => "Serialize and deserialize JSON with Newtonsoft.Json.";

    public string Usage => string.Empty;

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        SimpleWServer server = ExampleServer.Create(arguments);
        server.ConfigureJsonEngine(new NewtonsoftJsonEngine(NewtonsoftJsonEngine.SettingsSimpleWBuilder()));
        server.MapGet("/", () => new JsonMessage { Name = "SimpleW", Message = "Hello World !" });
        server.MapPost("/echo", (HttpSession session) => {
            JsonMessage message = session.JsonEngine.Deserialize<JsonMessage>(session.Request.BodyString);
            return new {
                received = message,
                serializer = session.JsonEngine.GetType().Name
            };
        });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

    private sealed class JsonMessage {

        public string Name { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

}
