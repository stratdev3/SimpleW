using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set CORS policy
            server.AddCORS(
                "*",                // Access-Control-Allow-Origin
                "*",                // Access-Control-Allow-Headers
                "GET,POST,OPTIONS", // Access-Control-Allow-Methods
                "true"              // Access-Control-Allow-Credentials
            );

            server.AddDynamicContent("/api");

            await server.RunAsync();
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            Console.ReadKey();
        }
    }

    public class SomeController : Controller {

        [Route("GET", "/test")]
        public object SomePublicMethod() {
            return new {
                message = "Hello World !"
            };
        }

    }

}