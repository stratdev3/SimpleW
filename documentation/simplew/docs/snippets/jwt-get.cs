using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/token")]
        public object Token() {
            var token = this.GetJwt()
            return "the token is {token}";
        }

    }

}