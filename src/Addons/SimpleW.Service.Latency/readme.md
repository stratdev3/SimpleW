# SimpleW.Service.Latency

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![License](https://img.shields.io/badge/license-MIT-7737d1.svg)](https://github.com/Stratdev3/SimpleW/blob/master/licence)
[![NuGet](https://img.shields.io/nuget/v/SimpleW.Service.Latency?color=7737d1&label=version&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.Latency)
[![Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.Latency?color=7737d1&label=downloads&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.Latency)

<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

Latency module for SimpleW that introduces artificial delays in HTTP responses for testing and debugging.

### Getting Started

The minimal API

```cs
using SimpleW;
using SimpleW.Observability;
using SimpleW.Service.Latency;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            server.UseLatencyModule(options => {
                options.GlobalLatency = TimeSpan.FromSeconds(1);
                options.Rules.Add(new LatencyRule("/api/*", TimeSpan.FromSeconds(2)));
            });

            // run server
            await server.RunAsync();
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
