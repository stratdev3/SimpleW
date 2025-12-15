using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
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