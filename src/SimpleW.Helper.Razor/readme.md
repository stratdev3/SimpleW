# SimpleW.Helper.Razor

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package the Razor Template Engine for SimpleW.

### Getting Started

The minimal API

```cs
using SimpleW;
using SimpleW.Helper.Razor;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set Newtonsoft.json as a JsonEngine
            server.ConfigureJsonEngine(new NewtonsoftJsonEngine());

            // return json as usual
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });
            // return razor view with anonymous model
            server.MapGet("/Home", () => {
                return RazorResults.View("Home", new { Title = "Home", H1 = "Welcome" })
            });
            // return razor view with type model and a viewbag
            server.MapGet("/api/user", (Guid id) => {
                // retrieve user from database
                var user = UserRepository.Get(id);
                // return view
                return RazorResults.View("User", user)
                                   .WithViewBag(vb => {
                                       vb.Title = "User";
                                   })
            });

            // enable razor module
            server.UseRazorModule(o => {
                //o.ViewsPath = "Views";
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();

        }
    }

}
```

The Home View

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

The User View

```html
@model dynamic
<!doctype html>
<html>
<head><title>@ViewBag.Title</title></head>
<body>
  <h1>User @Model.FullName.ToUpper()</h1>
</body>
</html>
```

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.