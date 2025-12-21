using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            await server.RunAsync();
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/raw")]
        public void Raw() {
            // as the string will be convert to byte without following the http response format
            // browser may show a warning when display this response
            Session.SendResponseBody("raw");
        }

    }

}