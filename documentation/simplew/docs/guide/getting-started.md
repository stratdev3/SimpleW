# Getting Started

Stop talking, show me the code !

<div class="images-inline">
    <a href="https://github.com/quozd/awesome-dotnet?tab=readme-ov-file#web-servers" target="_blank"><img src="/public/awesome.svg" alt="dotnet awesome" /></a>
    <a href="https://www.nuget.org/packages/SimpleW" target="_blank"><img src="https://img.shields.io/nuget/dt/SimpleW" alt="NuGet Downloads" /></a>
</div>


## Installation

Using the nuget package, always prefer the last version.

```sh
$ dotnet add package SimpleW --version 26.0.0-beta.20260215-1450
```

::: tip NOTE
Follow this [guide](./migrate-from-v16) if you migrate from v16.
:::


## Minimal Example

The following minimal example can be used for rapid prototyping :

::: code-group

```csharp:line-numbers [Program.cs]
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // minimal api
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }

}
```

:::


It uses the [`MapGet`](../reference/simplewserver.md#map) and [`MapPost`](../reference/simplewserver.md#map) methods, which are quite the same as asp.net core.


## Rest API Example

The following example build a REST API with a clear routing and controller/method structured class.

::: code-group

```csharp:line-numbers [Program.cs]
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs on port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all classes based on Controller class, and serve on the "/api" endpoint
            server.MapControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
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


Then just [open your browser](http://localhost:2015/api/test?name=Chris) to and you will see the `{ "message": "Hello Chris !" }` JSON response.


## Static Files Example

The following example serve statics files in your `c:\www` directory.

::: code-group

```csharp:line-numbers [Program.cs]
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // use the StaticFilesModule
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\";                      // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
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
        <h1>Welcome<h1>
        <p>Hello World !</p>
    </body>
</html>
```

:::


Then just [open your browser](http://localhost:2015/).


## FullStack Example

The following example will serve both static files in your `c:\www` directory and a REST API.

::: code-group

```csharp:line-numbers [Program.cs]
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

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

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
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

```html:line-numbers [C:\www\index.html]
<html>
    <head>
        <title>index</title>
    </head>
    <body>
        <h1>Welcome<h1>
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


Then just Then just [open your browser](http://localhost:2015/) and interact with the form.


<style>
.images-inline {
    display: flex;
    gap: 1rem;
    align-items: center;
    flex-wrap: wrap;
}
.images-inline > img {
    max-width: 30% !important;
    height: auto !important;
}
</style>