using System.Net;
using SimpleW;


namespace example {

    /// <summary>
    /// Test Program
    /// </summary>
    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args) {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }

    }


    /// <summary>
    /// Test Controller
    /// </summary>
    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/hello")]
        public object Hello(string? name = null) {

            if (string.IsNullOrWhiteSpace(name)) {
                return Response.MakeNotFoundResponse("you must set a name parameter");
            }

            // the return will be serialized to json
            return new {
                message = $"{name}, Hello World !"
            };
        }

    }

}
