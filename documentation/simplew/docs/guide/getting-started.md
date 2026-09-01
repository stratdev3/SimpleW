# Getting Started

Stop talking, show me the code !

<AddonMeta
    package="SimpleW"
    license="MIT"
    license-url="https://github.com/Stratdev3/SimpleW/blob/master/licence"
/>


## Installation

Using the NuGet package, always prefer the latest version.

```sh
dotnet add package SimpleW
```


## Minimal example

The following minimal example can be used for rapid prototyping :

::: code-group

```csharp:line-numbers [Program.cs]
using System;
using System.Net;
using SimpleW;
using SimpleW.Observability;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // minimal api
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            // run server
            await server.RunAsync();
        }
    }

}
```

:::

Then just open your browser to [http://localhost:2015/api/test](http://localhost:2015/api/test) and you will see the `{ "message": "Hello World !" }` JSON response.

::: info
See the [Templates](../addons/template-templates.md#simplew-minimal) addons to quickly create a minimal project.
:::


## Controller Example

The following example builds a REST API with clear routing using a structured controller/method class.

::: code-group

```csharp:line-numbers [Program.cs]
using System.Net;
using SimpleW;
using SimpleW.Observability;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // listen to all IPs on port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all classes based on Controller class, and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            // run server
            await server.RunAsync();
        }
    }

}
```

```csharp:line-numbers [TestController.cs]
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
```

:::


Then just open your browser to [http://localhost:2015/api/test?name=Chris](http://localhost:2015/api/test?name=Chris) and you will see the `{ "message": "Hello Chris !" }` JSON response.


## Static Files Example

The following example serves static files from your `c:\www` directory.

::: code-group

```csharp:line-numbers [Program.cs]
using System;
using System.Net;
using SimpleW;
using SimpleW.Observability;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // use the StaticFilesModule
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\";                      // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            // run server
            await server.RunAsync();
        }
    }
}
```

```html:line-numbers [C:\www\index.html]
<html>
    <head>
        <title>index</title>
    </head>
    <body>
        <h1>Welcome</h1>
        <p>Hello World !</p>
    </body>
</html>
```

:::


Then just open your browser to [http://localhost:2015/](http://localhost:2015/).


## FullStack Example

The following example serves both static files from your `c:\www` directory and a REST API.

::: code-group

```csharp:line-numbers [Program.cs]
using System;
using System.Net;
using SimpleW;
using SimpleW.Observability;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all classes based on Controller class, and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            // use the StaticFilesModule
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\";                      // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            // run server
            await server.RunAsync();
        }
    }

}
```

```csharp:line-numbers [TestController.cs]
using System;
using System.Net;
using SimpleW;

namespace Sample {

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/hello")]
        public object Hello(string? name = null) {

            if (string.IsNullOrWhiteSpace(name)) {
                return Response.NotFound("you must set a name parameter");
            }

            // the return will be serialized to json
            return new {
                message = $"{name}, Hello World !"
            };
        }

    }

}
```

```html:line-numbers [C:\www\index.html]
<html>
    <head>
        <title>index</title>
    </head>
    <body>
        <h1>Welcome</h1>
        <input type="text" id="nameInput" placeholder="Enter your name" />
        <button id="greetBtn">Greet me</button>
        <p id="hello"></p>
    </body>
    <script>
    document.addEventListener('DOMContentLoaded', () => {

        const input  = document.getElementById('nameInput');
        const button = document.getElementById('greetBtn');
        const output = document.getElementById('hello');

        button.addEventListener('click', () => {
            const name = input.value.trim();

            if (!name) {
                output.textContent = 'Please enter a name.';
                return;
            }

            fetch(`/api/test/hello?name=${encodeURIComponent(name)}`)
                .then(response => {
                    if (!response.ok) {
                        throw new Error(`HTTP error! status: ${response.status}`);
                    }
                    return response.json();
                })
                .then(data => {
                    output.textContent = data.message || 'No message in response';
                })
                .catch(err => {
                    console.error('Fetch error:', err);
                    output.textContent = 'Failed to fetch greeting.';
                });
        });
    });
    </script>
</html>
```

:::

Then just open your browser to [http://localhost:2015/](http://localhost:2015/) and interact with the form.
