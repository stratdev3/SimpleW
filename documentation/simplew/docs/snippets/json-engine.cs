using System.Net;
using SimpleW;
using SimpleW.Newtonsoft;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs on port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // 1. set Newtonsoft as the json engine
            server.ConfigureJsonEngine(new NewtonsoftJsonEngine());

            // or

            // 2. set Newtonsoft as the json engine with custom settings
            server.ConfigureJsonEngine(new NewtonsoftJsonEngine(
                (action) => {
                    Newtonsoft.Json.JsonSerializerSettings settings = new();

                    // you can customize settings dependings the IJsonEngine method called
                    if (action == nameof(IJsonEngine.Serialize)) {
                        settings.Formatting = Newtonsoft.Json.Formatting.Indented;
                    }

                    return settings;
                }
            ));

            // find all Controllers classes and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");

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
            var url = Request.RawUrl;

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