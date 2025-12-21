using System;
using System.Net;
using System.Net.Sockets;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // unix socket
            string unixSocketPath = @"C:\www\server.sock";
            var server = new SimpleWServer(new UnixDomainSocketEndPoint(unixSocketPath));

            // find all Controllers classes and serve on the "/api" endpoint
            server.UseControllers<Controller>("/api");

            Console.WriteLine(@"server available on : unix:C:\www\server.sock");

            // start a blocking background server
            await server.RunAsync();
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