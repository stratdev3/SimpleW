using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            // serve static content located in your folder "C:\www\" to "/" endpoint, cached for 24h
            server.AddStaticContent(@"C:\www", "/", timeout: TimeSpan.FromDays(1));

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

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