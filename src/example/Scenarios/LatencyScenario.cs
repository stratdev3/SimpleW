using SimpleW;
using SimpleW.Service.Latency;


namespace example;

internal sealed class LatencyScenario : IScenario {

    public string Name => "latency";

    public string Description => "Global and /api-specific artificial response latency.";

    public string Usage => " [--global-ms 250] [--api-ms 1000]";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("global-ms", "api-ms");

        int globalMilliseconds = arguments.GetInt32("global-ms", 250, 0, 60_000);
        int apiMilliseconds = arguments.GetInt32("api-ms", 1000, 0, 60_000);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseLatencyModule(options => {
            options.GlobalLatency = TimeSpan.FromMilliseconds(globalMilliseconds);
            options.Rules.Add(new LatencyRule("/api/*", TimeSpan.FromMilliseconds(apiMilliseconds)));
        });
        server.MapGet("/", () => new { route = "/", delayMs = globalMilliseconds });
        server.MapGet("/api/slow", () => new { route = "/api/slow", delayMs = apiMilliseconds });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
