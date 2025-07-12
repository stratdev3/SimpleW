# Basic Example


The RestAPI is based on **routes**, so just add a `RouteAttribute` to target **methods** of a `Controller` base class.<br />
The return is serialized into json and sent as a response to the client.

Use `server.AddDynamicContent()` to handle RestAPI.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

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

    // inherit from Controller to target a class
    public class SomeController : Controller {

        // use the Route attribute to target a public method
        [Route("GET", "/test")]
        public object SomePublicMethod() {
            // the return will be serialized to json
            return new {
                message = "Hello World !"
            };
        }

    }

}
```

Then just open your browser to http://localhost:2015/api/test and you will see the `{ "message": "Hello World !" }` json response.

Note : the controller __CAN NOT__ have constructor.
