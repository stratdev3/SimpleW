# Route Attribute

The primary goal of the `Route` attribut is to target method and add them to the [`Router`](./router).


## Properties

```csharp
/// <summary>
/// Host (optional)
/// </summary>
public string? Host { get; set; }
```

```csharp
/// <summary>
/// IsAbsolutePath (optional)
/// </summary>
public bool IsAbsolutePath { get; set; }
```

```csharp
/// <summary>
/// Description (optional)
/// </summary>
public string? Description { get; set; }
```


## Method

```csharp
/// <summary>
/// Initializes a new instance of the <see cref="RouteAttribute"/> class for Method.
/// </summary>
/// <param name="method">The method.</param>
/// <param name="path">The path.</param>
/// <exception cref="ArgumentException">The argument 'verb' must be specified.</exception>
/// <exception cref="ArgumentException">The argument 'path' must be specified.</exception>
public RouteAttribute(string method, string path)
```

Example, i want the `Home()` method of the `TestController` class be called when client send a request to http://localhost:2015/api/test/index.

```csharp{20}
using System;
using System.Net;
using SimpleW;
using SimpleW.Observability;

namespace Sample {

    class Program {
        static async Task Main() {
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            await server.RunAsync();
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


## Controller

```csharp
/// <summary>
/// Route Attribut for Controller
/// </summary>
/// <param name="path">The path.</param>
/// <exception cref="ArgumentException">The argument 'path' must be specified.</exception>
RouteAttribute(string path)
```

Example, i want all callable method of my `TestController` have the same prefix like /test.


```csharp{17}
using System;
using System.Net;
using SimpleW;
using SimpleW.Observability;

namespace Sample {

    class Program {
        static async Task Main() {
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            await server.RunAsync();
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