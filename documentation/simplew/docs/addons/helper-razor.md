# Razor <Badge type="tip" text="Official" />

The [`SimpleW.Helper.Razor`](https://www.nuget.org/packages/SimpleW.Helper.Razor) package is a Razor templating module for the SimpleW web server.
It enables server-side HTML rendering using Razor syntax (via RazorLight) and integrates with SimpleW’s routing/controller system by introducing a `ViewResult` that the module can render into an HTTP response.

This module is designed to feel familiar to ASP.NET MVC developers, while staying simple and minimal.


## Features

It allows you to :
- **Razor View Engine** : powered by RazorLight (compile + render)
- **Controller integration** : RazorController provides a `View()` helper
- **Models** : pass a DTO, an anonymous object, or an `ExpandoObject`; views access its public properties through a dynamic model
- **ViewBag** : `dynamic` ViewBag supported (`ExpandoObject`)
- **Layouts** : `Layout = "_Layout"` + `@RenderBody()` / `@RenderSection()`
- **Partials** : render partial templates through the injected `ViewBag.Html` helper
- **Html helper** : `PartialAsync()` returns non-escaped HTML content
- **Caching** : compiled templates cached in memory (RazorLight memory cache)
- **Error handling** : compilation and rendering errors return HTTP 500 with details (HTML-escaped)


## Requirements

- .NET 8.0, 9.0, or 10.0
- SimpleW (core server)
- RazorLight package (automatically included)


## Installation

```sh
$ dotnet add package SimpleW.Helper.Razor
```

See the [changelog](./helper-razor-changelog.md)


## Configuration options

| Option | Default | Description |
|---|---|---|
| `ViewsPath` | `"Views"` | Root directory passed to RazorLight for view lookup. A relative path is resolved from the process working directory; use an absolute path when that directory may vary. |
| `LayoutsPath` | `"Shared"` | Directory inside `ViewsPath` used as a fallback for unqualified layout names such as `"_Layout"`. |
| `PartialsPath` | `"Partials"` | Directory inside `ViewsPath` used as a fallback for unqualified partial names such as `"Header"`. |
| `ViewExtension` | `".cshtml"` | Template extension appended when `AutoAppendExtension` is enabled. Include the leading dot. |
| `AutoAppendExtension` | `true` | Appends `ViewExtension` to view, layout, and partial names that do not already end with it. Set it to `false` to require exact template names. |

Compiled templates always use RazorLight's in-memory cache. The module currently has no enable/disable, file-watching, cache toggle, default-layout, model-resolver, or render-hook option. A layout is selected inside each view with Razor's `Layout` property.


## Minimal example

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


### Layouts and Partials structure

If you use **layouts** and **partials**, a common (ASP.NET Core-like) structure is :

```text
/Views
   /Home
      Index.cshtml
      About.cshtml

   /Shared
      _Layout.cshtml

   /Partials
      _Header.cshtml
      _Footer.cshtml
```

> Defaults:
> - `LayoutsPath = "Shared"`
> - `PartialsPath = "Partials"`


### 2. Rendering a view

You can return a `ViewResult` directly from a mapped route :

::: code-group

```csharp [Controller Based]
using System;
using System.Net;
using SimpleW;
using SimpleW.Helper.Razor;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 8080);

            server.UseRazorModule(options => {
                options.ViewsPath = "Views"; // path of your views folder
            });
            server.MapControllers<RazorController>("/api");

            await server.RunAsync();
        }
    }

    [Route("/home")]
    public class HomeController : RazorController {

        [Route("GET", "/index")]
        public object Index() {
            // the model can be strongly typed or anonymous like in this example
            var model =  new { Title = "Home", H1 = "Welcome" };
            return View("Home/Index", model);
        }

    }

}
```

```csharp [Delegate based]
using System.Net;
using SimpleW;
using SimpleW.Helper.Razor;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 8080);

            server.UseRazorModule(options => {
                options.ViewsPath = "Views"; // path of your views folder
            });
            server.MapGet("/api/home/index", () => {
                // the model can be strongly typed or anonymous like in this example
                var model =  new { Title = "Home", H1 = "Welcome" };
                return RazorResults.View("Home/Index", model);
            });

            await server.RunAsync();
        }
    }
}
```

:::


### Layouts and Partials

#### Layouts

To use a layout, set `Layout` at the top of your view (similar to ASP.NET Core):

```cs
@{
    Layout = "_Layout";
    ViewBag.Title = "Home";
}
<h1>Hello</h1>

@section Scripts {
    <script>console.log("home loaded");</script>
}
```

Create the layout file in `Views/Shared/_Layout.cshtml` (default `LayoutsPath = "Shared"`):

```
@{
    dynamic Html = ViewBag.Html;
}
<!doctype html>
<html>
<head>
    <meta charset="utf-8" />
    <title>@ViewBag.Title</title>
</head>
<body>
    @await Html.PartialAsync("Header")

    <main>
        @RenderBody()
    </main>

    @RenderSection("Scripts", required: false)
</body>
</html>
```

- `@RenderBody()` injects the view HTML.
- `@RenderSection(name, required: false)` renders an optional section declared with `@section`.

#### Partials

Partials follow the usual underscore convention. Create a partial such as:

`Views/Partials/_Header.cshtml` (default `PartialsPath = "Partials"`)

```html
<header>
    <h2>SimpleW</h2>
</header>
```

Render it from a view or layout:

```csharp
@await Html.PartialAsync("Header")
```

`PartialAsync()` returns a non-escaped HTML content wrapper, so the browser receives real HTML (not `&lt;div&gt;...`). The `Html` variable above is a local alias for the helper injected as `ViewBag.Html`.


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

A view name is a **path relative to** `ViewsPath`.

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

## `Html` helper in templates

The module injects a `RazorModule.SimpleHtmlHelper` instance as `ViewBag.Html` before rendering the requested view. RazorLight does not declare a compile-time `Html` property, so create a local alias in every view or layout that calls the helper:

```csharp
@{
    dynamic Html = ViewBag.Html;
}
@await Html.PartialAsync("Header")
```

The helper exposes `PartialAsync(string name)` and `PartialAsync(string name, object? model)`. Lookup checks the requested name and its underscore variant, then the configured `PartialsPath` and `LayoutsPath`. With the default options, `"Header"` can therefore resolve to `Views/Partials/_Header.cshtml`.

The package does not provide a `SimpleWTemplatePage` base class or automatically expose a direct `Html` property. Code blocks such as the alias above belong in the template itself, not in `_ViewImports.cshtml`.


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

Other rendering failures also return HTTP **500** with an HTML-escaped exception. This is helpful in development, but exposes implementation details; the current module does not provide an option to replace these responses with a generic production error page.


## Performance Considerations

- **Template Caching** : RazorLight caches compiled templates in memory
- **First Request** : Initial compilation has overhead, subsequent requests are fast
- **Memory** : Each unique template is cached separately


## See Also

- [RazorLight Documentation](https://github.com/toddams/RazorLight)
- [Razor Syntax Reference](https://learn.microsoft.com/en-us/aspnet/core/razor-pages/)
