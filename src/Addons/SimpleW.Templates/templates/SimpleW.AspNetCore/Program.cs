using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using SimpleW;
using SimpleW.Helper.Hosting;
using SimpleW.Helper.Razor;


namespace ModuleName {

    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static async Task Main(string[] args) {

            var builder = SimpleWHost.CreateApplicationBuilder(args)
                                     .UseMicrosoftLogging();

            builder.ConfigureSimpleW(
                configureApp: server => {

                    // razor
                    server.UseRazorModule(options => {
                        options.ViewsPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Views");
                    });

                    // find all classes based on Controller class, and serve on the "/api" endpoint
                    server.MapControllers<RazorController>("");

                    // network options for better performances
                    server.UseEngine(options => {
                        options.ReuseAddress = true;
                        options.TcpNoDelay = true;
                        options.TcpKeepAlive = true;
                        options.AcceptPerCore = true;
                    });

                    // subscribe to events
                    server.OnStateChanged((s, state) => {
                        if (state != SimpleWServerState.Started) {
                            return;
                        }
                        Process.Start(new ProcessStartInfo {
                            FileName = $"http://localhost:{s.Port}/home/index?name=Chris",
                            UseShellExecute = true
                        });
                    });
                }
            );

            // run and block
            var host = builder.Build();
            await host.RunAsync();
        }

    }

}
