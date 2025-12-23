using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {

        static void Main(string[] args) {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directory which contains the index.html
            server.AddStaticContent(@"C:\www\client", "/", timeout: TimeSpan.FromDays(1));

            // find all Controllers classes and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            await server.RunAsync();
            Console.WriteLine("http server started at http://localhost:{server.Port}/");
            Console.WriteLine("sse server started at ws://localhost:{server.Port}/api/test/sse");

            // menu
            while (true) {
                Console.WriteLine("\nMenu : (S)end or (Q)uit ?\n");
                var key = Console.ReadKey().KeyChar.ToString().ToLower();
                if (key == "q") {
                    Environment.Exit(0);
                }
                if (key == "s") {
                    Console.WriteLine($"\nsend hello to all clients\n");
                    // multicast message to all administrator
                    server.BroadcastSSESessions("message", "hello", (session) => session.webuser.Profile == "Administrator");
                }
            }

        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // SSE session is initiating with a GET method from client
        [Route("GET", "/sse")]
        public object SSE() {

            // sse only for authenticated user
            if (webuser.Id == Guid.Empty) {
               return MakeAccessResponse();
            }

            // elevate the current session as a SSE Sessions
            AddSSESession();
            // return SSE stream response to client
            return MakeServerSentEventsResponse();
        }

    }

}