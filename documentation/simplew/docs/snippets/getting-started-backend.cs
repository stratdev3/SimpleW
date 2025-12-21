using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/hello")]
        public object Hello(string? name = null) {

            if (string.IsNullOrWhiteSpace(name)) {
                return MakeNotFoundResponse("you must set a name parameter");
            }

            // the return will be serialized to json
            return new {
                message = $"{name}, Hello World !"
            };
        }

    }

}