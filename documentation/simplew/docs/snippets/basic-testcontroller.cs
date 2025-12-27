using System.Net;
using SimpleW;

namespace Sample {

    // inherit from Controller
    public class SomeController : Controller {

        // use the Route attribute to target a public method
        [Route("GET", "/test")]
        public object SomePublicMethod(string name = "World") {
            // the return will be serialized to json and sent as response to client
            return new {
                message = $"Hello {name} !"
            };
        }

    }

}
