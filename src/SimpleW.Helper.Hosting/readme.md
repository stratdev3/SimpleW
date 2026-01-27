# SimpleW.Helper.Hosting

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://stratdev3.github.io/SimpleW)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package is an integration layer between SimpleW and Microsoft.Extensions.Hosting.

### Getting Started

The minimal API

```cs
using Microsoft.Extensions.Hosting;
using SimpleW;
using SimpleW.Helper.Hosting;

namespace Sample {
    class Program {

        static async Task Main() {

            var builder = SimpleWHost.CreateApplicationBuilder(args);

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

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.