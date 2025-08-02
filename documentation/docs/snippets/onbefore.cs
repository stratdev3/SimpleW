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

        // Router will call this methods before another one
        public override void OnBeforeMethod() {
            Console.WriteLine("OnBeforeMethod()");
        }

        [Route("GET", "/index")]
        public object Index() {
            return "test index page";
        }

        [Route("POST", "/create")]
        public object Create() {
            return "test create success";
        }

    }

}