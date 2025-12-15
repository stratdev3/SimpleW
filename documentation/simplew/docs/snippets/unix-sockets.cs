using System;
using System.Net;
using System.Net.Sockets;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // unix socket
            string unixSocketPath = @"C:\www\server.sock";
            var server = new SimpleWServer(new UnixDomainSocketEndPoint(unixSocketPath));

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            // start non blocking background server
            server.Start();

            Console.WriteLine(@"server available on : unix:C:\www\server.sock");

            // block console for debug
            Console.ReadKey();

        }
    }

    // inherit from Controller
    public class SomeController : Controller {

        // use the Route attribute to target a public method
        [Route("GET", "/test")]
        public object SomePublicMethod() {

            // the Request property contains all data (Url, Headers...) from the client Request
            var url = Request.Url;

            // the return will be serialized to json and sent as response to client
            return new {
                message = Message()
            };
        }

        private string Message() {
            return "Hello World !";
        }

    }

}