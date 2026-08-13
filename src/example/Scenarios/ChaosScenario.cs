using SimpleW;
using SimpleW.Service.Chaos;


namespace example;

internal sealed class ChaosScenario : IScenario {

    public string Name => "chaos";

    public string Description => "Reproducible failures and delays under the /api prefix.";

    public string Usage => " [--probability 0.5] [--delay-ms 250] [--seed 42]";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("probability", "delay-ms", "seed");

        double probability = arguments.GetDouble("probability", 0.5, 0, 1);
        int delayMilliseconds = arguments.GetInt32("delay-ms", 250, 0, 60_000);
        int seed = arguments.GetInt32("seed", 42, int.MinValue, int.MaxValue);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseChaosModule(options => {
            options.Enabled = true;
            options.Prefix = "/api";
            options.Probability = probability;
            options.CloseConnectionProbability = 0;
            options.MinDelayMs = delayMilliseconds;
            options.MaxDelayMs = delayMilliseconds;
            options.FixedStatusCode = 503;
            options.Seed = seed;
            options.BodyTemplate = "CHAOS: simulated error {code} {text}";
        });
        server.MapGet("/", () => new { message = "This route is outside the chaos prefix." });
        server.MapGet("/api/test", () => new { message = "This response survived chaos." });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
