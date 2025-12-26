using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set CORS policy
            server.UseCorsModule(o => {
                o.Prefix = "/api";
                o.AllowedOrigins = new[] { "http://localhost:2015" };
                o.AllowCredentials = true;
                o.AllowedMethods = "GET, POST, OPTIONS";
                o.MaxAgeSeconds = 600;
            });

            server.MapControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
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