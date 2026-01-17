# Unix Sockets

[Unix Domain Socket](https://en.wikipedia.org/wiki/Unix_domain_socket) (UDS) can also be used as an entrypoint for the server.
They are supported on : **Linux**, **MacOS**, **Android**... and even **Windows** !

With just a small change, the [basic api example](./getting-started.md#minimal-example) can also be served over a Unix socket.


```csharp:line-numbers{13}
using System;
using System.Net;
using System.Net.Sockets;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // unix socket
            string unixSocketPath = @"C:\www\server.sock";
            var server = new SimpleWServer(new UnixDomainSocketEndPoint(unixSocketPath));

            // find all Controllers classes and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            Console.WriteLine(@"server available on : unix:C:\www\server.sock");

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
```

You can use `curl` to test :

```
$ curl --unix-socket C:\www\test.sock http://localhost/api/test
> { "message" : "Hello World !" }
```

There only one change :
- L13 : use the `SimpleWServer()` constructor with `UnixDomainSocketEndPoint` argument.

