# Advanced


## Catch All Requests

You can setup a maintenance page to catch all api call by using the wildcard in a ```RouteAttribute```.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // need by MaintenanceController wildcard route parameter
            server.Router.RegExpEnabled = true;
            // add the dedidacted controller
            server.AddDynamicContent(typeof(MaintenanceController), "/api/v1");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }

    // inherit from Controller to target a class
    public class MaintenanceController : Controller {

        // wildcard route parameter will call all string under root api
        [Route("GET", "/*")]
        public object Maintenance() {
            return Response.MakeErrorResponse(503, "Maintenance");
        }

    }

}
```


## Properties

`Controller` class containers some useful properties.

#### Request

You can access the `Request` property inside any controller.


#### Responses

You can access the `Response` property inside any controller.
