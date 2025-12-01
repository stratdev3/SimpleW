using System.Net;


namespace Core {

    /// <summary>
    /// Example Program
    /// perf : bombardier -c 200 -d 10s http://127.0.0.1:8080/api/test/hello
    /// </summary>
    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static async Task Main(string[] args) {
            await Rewrite();
            //await Raw();
        }

        static async Task Rewrite() {
            SimpleW.SimpleW server = new(IPAddress.Any, 8080);
            server.MapGet("/api/test/hello", static (req, ctx) => {
                return ctx.SendJsonAsync(new { message = "Hello World !" });
            });
            server.MapGet("/api/test/text", static (req, ctx) => {
                return ctx.SendTextAsync("Hello World !");
            });
            server.OptionReuseAddress = true;
            server.OptionNoDelay = true;
            server.OptionKeepAlive = true;
            server.OptionRunAcceptSocketPerCore = true;
            server.OptionReceiveStrategy = SimpleW.ReceivedStrategy.NetworkStream;

            // start non blocking background server
            CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"server started at http://localhost:{server.Port}/api/test/hello");
            await server.StartAsync(cts.Token);
            Console.WriteLine("server stopped");
        }

        static async Task Raw() {
            RawServer.RawServer server = new(IPAddress.Any, 8080);

            server.OptionReuseAddress = true;
            server.OptionNoDelay = true;
            server.OptionKeepAlive = true;

            // start non blocking background server
            CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"server started at http://localhost:{server.Port}/api/test/hello");
            await server.StartAsync(cts.Token);
            Console.WriteLine("server stopped");
        }

    }

}
