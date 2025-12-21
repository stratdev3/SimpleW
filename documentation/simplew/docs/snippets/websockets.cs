using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directory which contains the index.html
            server.AddStaticContent(@"C:\www\client", "/", TimeSpan.FromDays(1));

            // find all Controllers class and serve on the "/websocket/" endpoint
            server.AddWebSocketContent("/websocket");

            await server.RunAsync();
            Console.WriteLine("http server started at http://localhost:{server.Port}/");
            Console.WriteLine("websocket server started at ws://localhost:{server.Port}/websocket");

            // menu
            while (true) {
                Console.WriteLine("\nMenu : (S)end or (Q)uit ?\n");
                var key = Console.ReadKey().KeyChar.ToString().ToLower();
                if (key == "q") {
                    Environment.Exit(0);
                }
                if (key == "s") {
                    // multicast message to all connected sessions
                    Console.WriteLine($"\nsend hello to all clients\n");
                    server.MulticastText("hello");
                }
            }

        }
    }

}