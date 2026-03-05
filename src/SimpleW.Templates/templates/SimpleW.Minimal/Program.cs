using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using SimpleW;
using SimpleW.Observability;


namespace ModuleName {

    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static async Task Main(string[] args) {

#if DEBUG
            // log for debug
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Trace);
#endif

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
                Process.Start(new ProcessStartInfo {
                    FileName = $"http://localhost:{server.Port}",
                    UseShellExecute = true
                });
            };

            // run and block
            await server.RunAsync();
        }

    }

}
