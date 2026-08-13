using SimpleW;
using SimpleW.Modules;


namespace example;

internal sealed class ServerSentEventsScenario : IScenario {

    private const string EventPage = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>SimpleW SSE</title></head>
        <body>
          <h1>Server-Sent Events</h1>
          <pre id="log"></pre>
          <script>
            const output = document.querySelector('#log');
            const source = new EventSource('/sse');
            source.addEventListener('status', event => output.textContent += `status: ${event.data}\n`);
            source.addEventListener('tick', event => output.textContent += `${event.data}\n`);
            source.onerror = () => output.textContent += 'connection interrupted\n';
          </script>
        </body>
        </html>
        """;

    public string Name => "sse";

    public string Description => "Browser EventSource client receiving a periodic server tick.";

    public string Usage => string.Empty;

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        ServerSentEventsHub? hub = null;
        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/", (HttpSession session) => session.Response.Html(EventPage));
        server.UseServerSentEventsModule(options => {
            options.Prefix = "/sse";
            options.AllowAnyOrigin = false;
            options.AutoJoinRoom = "__all";
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.OnConnect = async (connection, _) => {
                await connection.SendEventAsync("connected", "status").ConfigureAwait(false);
            };
            hub = options.Hub;
        });

        using CancellationTokenSource loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task broadcastTask = BroadcastTicksAsync(hub!, loopCancellation.Token);
        try {
            await ExampleServer.RunAsync(server, "http", arguments, loopCancellation.Token).ConfigureAwait(false);
        }
        finally {
            loopCancellation.Cancel();
            await broadcastTask.ConfigureAwait(false);
        }
    }

    private static async Task BroadcastTicksAsync(ServerSentEventsHub hub, CancellationToken cancellationToken) {
        int counter = 0;
        try {
            while (true) {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                counter++;
                await hub.BroadcastAsync("__all", new ServerSentEventsMessage {
                    Event = "tick",
                    Payload = $"tick #{counter} @ {DateTimeOffset.UtcNow:O}"
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

}
