using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.UseControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
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