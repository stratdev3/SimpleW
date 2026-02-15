using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using SimpleW;


namespace ModuleName {

    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static async Task Main(string[] args) {

            // server
            SimpleWServer server = new(IPAddress.Any, 2015);

            server.Configure(options => {
                // network options for better performances
                options.ReuseAddress = true;
                options.TcpNoDelay = true;
                options.TcpKeepAlive = true;
                options.AcceptPerCore = true;
            });

            // default handler
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });

            // subscribe to events
            server.OnStarted += (object? sender, EventArgs e) => {
                string url = $"http://localhost:{server.Port}";
                Console.WriteLine($"server started at {url}");
                Process.Start(new ProcessStartInfo {
                    FileName = url,
                    UseShellExecute = true
                });
            };
            server.OnStopped += (object? sender, EventArgs e) => {
                Console.WriteLine("server stopped");
            };

            // run and block
            await server.RunAsync();
        }

    }

}
