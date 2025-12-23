using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs on port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all classes based on Controller class, and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }

}