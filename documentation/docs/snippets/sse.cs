using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {

        static void Main(string[] args) {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directory which contains the index.html
            server.AddStaticContent(@"C:\www\client", "/");

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("sse server started at ws://localhost:2015/api/test/sse");

            // menu
            while (true) {
                Console.WriteLine("\nMenu : (S)end or (Q)uit ?\n");
                var key = Console.ReadKey().KeyChar.ToString().ToLower();
                if (key == "q") {
                    Environment.Exit(0);
                }
                if (key == "s") {
                    // multicast message to all connected sessions
                    Console.WriteLine($"\nsend hello to all clients\n");
                    server.BroadcastSSESessions("message", "hello");
                }
            }

        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // SSE session is initiating with a GET method from client
        [Route("GET", "/sse")]
        public object SSE() {
            // elevate the current session as a SSE Sessions
            AddSSESession();
            // return SSE stream response to client
            return MakeServerSentEventsResponse();
        }

    }

}