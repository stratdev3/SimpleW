# SimpleW.Newtonsoft

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/docs/public/simplew-og.png)](https://stratdev3.github.io/SimpleW)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package add the Newtonsoft.Json support for the SimpleW package.

### Getting Started

The minimal API

```cs
using System;
using System.Net;
using SimpleW;
using SimpleW.Newtonsoft;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set Newtonsoft.json as a JsonEngine
            server.JsonEngine = new NewtonsoftJsonEngine();

            // minimal api
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }

}
```

## Documentation

To check out docs, visit [stratdev3.github.io](https://stratdev3.github.io/SimpleW).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.