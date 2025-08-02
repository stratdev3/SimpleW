# Route Attribute

The primary goal of the `Route` attribut is to target method and add them to the `Router`.


## RouteAttribute()

### Method

```csharp
/// <summary>
/// Route Attribut for Method
/// </summary>
/// <param name="method">The method.</param>
/// <param name="path">The path.</param>
/// <param name="queryStringMappingEnabled">The queryStringMappingEnabled.</param>
/// <param name="isAbsolutePath">The absolutePath.</param>
/// <param name="description">The string description for this route</param>
/// <exception cref="ArgumentException">The argument 'verb' must be specified.</exception>
/// <exception cref="ArgumentException">The argument 'path' must be specified.</exception>
RouteAttribute(string method, string path, bool queryStringMappingEnabled = true, bool isAbsolutePath = false, string description = null)
```

Example, i want the `Home()` method of the `TestController` class be called when client send a request to http://localhost:2015/api/test/index.

```csharp:line-numbers{19}
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

        // http://localhost:2015/api/test/index
        [Route("GET", "test/index")]
        public List<string> Index() {
            return new List<string>() {
                "foo", "bar"
            };
        }

        public object About() {
            return "about page";
        }

        private void Create() {
            // do stuff
        }

    }

}
```


### Controller

```csharp
/// <summary>
/// Route Attribut for Controller
/// </summary>
/// <param name="path">The path.</param>
/// <exception cref="ArgumentException">The argument 'path' must be specified.</exception>
RouteAttribute(string path)
```

Example, i want all callable method of my `TestController` have the same prefix like /test.


```csharp:line-numbers{17}
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

    [Route("test/")]
    public class TestController : Controller {

        // http://localhost:2015/api/test/index
        [Route("GET", "/index")]
        public List<string> Index() {
            return new List<string>() {
                "foo", "bar"
            };
        }

        // http://localhost:2015/api/test/about
        [Route("GET", "/about")]
        public object About() {
            return "about page";
        }

        private void Create() {
            // do stuff
        }

    }

}
```