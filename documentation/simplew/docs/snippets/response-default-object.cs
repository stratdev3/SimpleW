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

        [Route("GET", "/test")]
        public object Test() {
            return new {
                message = "Hello World !",
                current = DateTime.Now,
                i = 0,
                enable = true,
                d = new Dictionary<string, string>() { { "Foo", "Bar" } }
            };
        }

    }

}