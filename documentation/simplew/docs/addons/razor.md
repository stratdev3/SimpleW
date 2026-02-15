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
- **Layouts** : `Layout = "_Layout"` + `@RenderBody()` / `@RenderSection()`
- **Partials** : `@await Html.PartialAsync("Header")` (returns non-escaped HTML content)
- **Html helper** : `Html.PartialAsync()` available in templates (ASP.NET Core-like)
- **Caching** : compiled templates cached in memory (RazorLight memory cache)
- **Error handling** : compilation errors return HTTP 500 with details (HTML-escaped)


## Requirements

- .NET 8.0
- SimpleW (core server)
- RazorLight package (automatically included)

No external dependencies.


## Installation

```sh
$ dotnet add package SimpleW.Helper.Razor --version 26.0.0-beta.20260216-1463
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


### Layouts and Partials

#### Layouts

To use a layout, set `Layout` at the top of your view (similar to ASP.NET Core):

```cshtml
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

```cshtml
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

```cshtml
<header>
    <h2>SimpleW</h2>
</header>
```

Render it from a view or layout:

```cshtml
@await Html.PartialAsync("Header")
```

`PartialAsync()` returns a non-escaped HTML content wrapper, so the browser receives real HTML (not `&lt;div&gt;...`).


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

> Note: the module also injects an `Html` helper (ASP.NET Core-like) that currently exposes `Html.PartialAsync(...)`. If for any reason your template cannot resolve `Html`, you can always use `ViewBag.Html` directly.

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

In RazorLight, `ViewBag` values are available at runtime, but symbols like `Html` must exist at **compile time**.
That means you **cannot** rely on a `@{ dynamic Html = ViewBag.Html; }` block inside `_ViewImports.cshtml`
(because `_ViewImports` is for directives like `@using` / `@inherits`, and code blocks may not run as you expect).

SimpleW injects an `Html` helper into `ViewBag` (from `RazorModule`):

- `ViewBag["Html"] = new SimpleHtmlHelper(...)`

To be able to write **ASP.NET Core-style** calls:

```cshtml
@await Html.PartialAsync("Header")
```

you have two options:

### Option A (recommended): expose `Html` via a template base class

Create a base class that exposes `Html` as a real property:

```csharp
using RazorLight;
using RazorLight.Razor;

namespace SimpleW.Helper.Razor;

public abstract class SimpleWTemplatePage<TModel> : TemplatePage<TModel>
{
    public RazorModule.SimpleHtmlHelper Html
        => (RazorModule.SimpleHtmlHelper)ViewBag.Html!;
}
```

Then ensure the project injects an `@inherits` import at compile time (works even if your RazorLight
builder does **not** have `ConfigureRazor` / `AddDefaultImports`):

```csharp
// inside SimpleWRazorProject.GetImportsAsync(...)
imports.Insert(0, new TextSourceRazorProjectItem(
    key: "_SimpleW_HtmlImport",
    content: "@inherits SimpleW.Helper.Razor.SimpleWTemplatePage<dynamic>"
));
```

After that, every view/layout/partial can use:

```cshtml
@await Html.PartialAsync("Header")
```

without repeating anything in each template.

### Option B (fallback): define `Html` in each view/layout

If you do not want the base-class approach, you can define `Html` at the top of each template:

```cshtml
@{
    dynamic Html = ViewBag.Html;
}
```

Then use:

```cshtml
@await Html.PartialAsync("Header")
```

> Note: placing the `@{ ... }` alias inside `_ViewImports.cshtml` is not reliable.


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
