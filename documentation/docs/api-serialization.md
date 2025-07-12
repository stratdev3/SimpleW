# Serialization


## Default

The return of the `method` will be serialized to json using the excellent `JsonConvert.SerializeObject()` from [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/)

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {
        [Route("GET", "/test")]
        public object Test() {
            return new {
                message = "Hello World !",
                current = DateTime.Now,
                i = 0,
                enable = true,
                d = new Dictionary<string, string>() { { "Foo", "Bar" } }
            };
        }
    }

}
```

Requesting to `http://localhost:2015/api/test` will result to

```json
{
    "message": "Hello World !",
    "current": "2024-03-01T13:17:29.1249399+01:00",
    "i": 0,
    "enable": true,
    "d": {"Foo":"Bar"}
}
```
