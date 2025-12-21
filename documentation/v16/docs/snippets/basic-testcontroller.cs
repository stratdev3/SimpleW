using System.Net;
using SimpleW;

namespace Sample {

    // inherit from Controller
    public class SomeController : Controller {

        // use the Route attribute to target a public method
        [Route("GET", "/test")]
        public object SomePublicMethod(string name = "World") {
            // Query param passed in through function params

            // the Request property contains all data (Url, Headers...) from the client Request
            var url = Request.Url;

            // the return will be serialized to json and sent as response to client
            return new {
                message = Message()
            };
        }

        private string Message(string name) {
            return $"Hello {name} !";
        }

    }

}
