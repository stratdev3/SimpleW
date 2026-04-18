# SimpleW.Helper.Hosting

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Helper.Hosting)](https://www.nuget.org/packages/SimpleW.Helper.Hosting)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Helper.Hosting)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package is an integration layer between SimpleW and Microsoft.Extensions.Hosting.

### Getting Started

Minimal example

```cs
using Microsoft.Extensions.Hosting;
using SimpleW.Helper.Hosting;

namespace Sample {
    class Program {

        static async Task Main(string[] args) {

            var builder = SimpleWHost.CreateApplicationBuilder(args)
                                     .UseMicrosoftLogging();

            builder.ConfigureSimpleW(server => {
                server.MapGet("/api/test", () => {
                    return new { message = "Hello World !" };
                });
            });

            var host = builder.Build();
            await host.RunAsync();
        }
    }

}
```

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

If you need access to the built `IServiceProvider` while configuring the server, use the overload:

```cs
builder.ConfigureSimpleW((services, server) => {
    // resolve services or enable modules that depend on the host service provider
});
```

With the `SimpleW.Helper.DependencyInjection` package, that overload becomes the cleanest way to enable controller DI:

```cs
using Microsoft.Extensions.DependencyInjection;
using SimpleW.Helper.DependencyInjection;
using SimpleW.Helper.Hosting;

var builder = SimpleWHost.CreateApplicationBuilder(args);
builder.Services.AddScoped<MyService>();

builder.ConfigureSimpleW((services, server) => {
    server.UseDependencyInjection(services);
    server.MapController<HomeController>("/api");
});
```

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
