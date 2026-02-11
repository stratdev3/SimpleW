# Razor

The [`SimpleW.Helper.Razor`](https://www.nuget.org/packages/SimpleW.Helper.Razor) is a Razor templating module for the SimpleW web server.
It enables server-side HTML rendering using Razor syntax (via RazorLight) and integrates with SimpleW’s routing/controller system by introducing a ViewResult that the module can render into an HTTP response.

This module is designed to feel familiar to ASP.NET MVC developers, while staying simple and minimal.


## Features

Features

- **Razor View Engine** : powered by RazorLight (compile + render)
- **Controller integration** : RazorController provides a `View()` helper
- **Models** : pass a DTO or an anonymous object
- **ViewBag** : `dynamic` ViewBag supported (`ExpandoObject`)
- **Caching** : compiled templates cached in memory (RazorLight memory cache)
- **Error handling** : compilation errors return HTTP 500 with details (HTML-escaped)


## Requirements

- .NET 8.0
- SimpleW (core server)
- RazorLight package (automatically included)

No external dependencies.


## Installation

```sh
$ dotnet add package SimpleW.Helper.Razor --version 26.0.0-beta.20260211-1417
```


## Basic Usage

### 1. Folder Structure

Recommended layout :

::: code-group

``` [Controller Based]
/Views
   /Home
      Index.cshtml
      About.cshtml
/Controllers
   HomeController.cs
/Program.cs
```

``` [Delegate based]
/Views
   /Home
      Index.cshtml
      About.cshtml
/Program.cs
```

:::


### 2. Rendering a view

You can return a `ViewResult` directly from a mapped route :

::: code-group

```csharp [Controller Based]
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 8080);

            server.UseRazorModule(options => {
                options.Views = "Views" // path of your views folder
            });
            server.MapControllers<RazorController>("/api");

            await server.RunAsync();
        }
    }

    [Route("/home")]
    public sealed class HomeController : RazorController {

        [Route("GET", "/index")]
        public object Index() {
            // the model can for strongly type or anonymous like in this example
            var model =  new { Title = "Home", H1 = "Welcome" };
            return View("Home/Index", model);
        }

    }

}
```

```csharp [Delegate based]
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 8080);

            server.UseRazorModule(options => {
                options.Views = "Views" // path of your views folder
            });
            server.MapGet("/api/home/index", () => {
                // the model can for strongly type or anonymous like in this example
                var model =  new { Title = "Home", H1 = "Welcome" };
                return RazorResults.View("Home/Index", model);
            });

            await server.RunAsync();
        }
    }
}
```

:::


### 3. Create a View

`Views/Home/Index.cshtml` :

```html
@model dynamic
<!doctype html>
<html>
<head><title>@Model.Title</title></head>
<body>
    <h1>@Model.H1</h1>
</body>
</html>
```


## View names

A view name is a **path relative** to `ViewsPath`.

Examples (with `ViewsPath = "Views"`) :
- `"Home"` → `Views/Home.cshtml`
- `"Home/Index"` → `Views/Home/Index.cshtml`


## ViewBag Usage

`ViewResult` exposes a `ViewBag` (dynamic) using an `ExpandoObject`.
Use `WithViewBag()`:

```csharp
return RazorResults.View("Home.cshtml", new { Title = "Home" })
                   .WithViewBag(vb => {
                        vb.UserName = "Chris";
                        vb.Now = DateTimeOffset.UtcNow;
                    });
```

In the Razor view :

```html
<h2>Hello @ViewBag.UserName</h2>
<p>UTC: @ViewBag.Now</p>
```

## Status code and content type

`ViewResult` lets you specify status code and content type.

```csharp
return new ViewResult(
    name: "Home/Index",
    model: new { Title = "Home" },
    statusCode: 200,
    contentType: "text/html; charset=utf-8"
);
```


## Error handling

If Razor compilation fails, the module returns :
- HTTP **500**
- an HTML page containing the compilation error (HTML-escaped)

This is helpful in development. In production you might want to replace it with a generic page.


## Performance Considerations

- **Template Caching** : RazorLight caches compiled templates in memory
- **First Request** : Initial compilation has overhead, subsequent requests are fast
- **Development** : Consider disabling caching during development for live updates
- **Memory** : Each unique template is cached separately


## See Also

- [RazorLight Documentation](https://github.com/toddams/RazorLight)
- [Razor Syntax Reference](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/)
