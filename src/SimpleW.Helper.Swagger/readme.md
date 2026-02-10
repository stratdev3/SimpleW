# SimpleW.Helper.Swagger

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package provides Swagger / OpenAPI support for SimpleW.

### Getting Started

The minimal API

```cs
using SimpleW;
using SimpleW.Helper.Swagger;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            server.MapGet("/swagger.json", static (HttpSession session) => {
                return Swagger.Json(session, options => {
                    options.Title = "My API";
                    options.Version = "v1";
                });
            });

            server.MapGet("/admin/swagger", static (HttpSession session) => {
                return Swagger.Ui(session, "/swagger.json", options => {
                    options.Title = "My API";
                    options.Version = "v1";
                });
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();

        }
    }

}
```

Open http://localhost:{server.Port}/admin/swagger to see Swagger UI or http://localhost:{server.Port}/swagger.json for the json version only

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.