using System.Net;
using SimpleW;
using SimpleW.Newtonsoft;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs on port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set Newtonsoft as the json engine with recommended settings
            server.JsonEngine = new NewtonsoftJsonEngine(NewtonsoftJsonEngine.SettingsSimpleWBuilder());

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

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