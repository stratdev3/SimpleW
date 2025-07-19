# Getting Started 

## Installation

Using the [SimpleW](https://www.nuget.org/packages/SimpleW) nuget package, always prefer the last [version](https://github.com/stratdev3/SimpleW/blob/master/release.md).

```cmd
$ dotnet add package SimpleW
```

::: tip NOTE
SimpleW depends on [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) package for json serialization/deserialization.
It will be replaced in futur by the native `System.Text.Json` as long as
some advanced features will be covered (`Populate` and `streamingContextObject`, see [work-in-progress](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/migrate-from-newtonsoft?pivots=dotnet-8-0#table-of-differences)).
:::


## API Example


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

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/hello")]
        public object Hello(string? name = null) {

            if (string.IsNullOrWhiteSpace(name)) {
                return MakeNotFoundResponse("you must set a name parameter");
            }

            // the return will be serialized to json
            return new {
                message = $"{name}, Hello World !"
            };
        }

    }

}
```

Then just open your browser to http://localhost:2015/api/test/hello?name=Chris and you will see the `{ "message": "Chris, Hello World !" }` json response.


## Static Files Example

To serve statics files in your `c:\www` directory with very few lines of code :

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve static content located in your folder "C:\www\" to "/" endpoint
            server.AddStaticContent(@"C:\www", "/");

            // enable autoindex if no index.html exists in the directory
            server.AutoIndex = true;

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}
```

Then just point your browser to http://localhost:2015/.

