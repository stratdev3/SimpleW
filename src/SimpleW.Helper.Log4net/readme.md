# SimpleW.Helper.Log4net

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package bridges SimpleW internal logging to Log4net.

### Getting Started

The minimal API

```cs
using System;
using System.Net;
using SimpleW;
using SimpleW.Helper.Log4net;
using log4net;
using log4net.Appender;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace Sample {
    class Program {

        static async Task Main() {

            // configure log4net console logger
            var hierarchy = (Hierarchy)LogManager.GetRepository();

            var layout = new PatternLayout("%date %-5level %message%newline");
            layout.ActivateOptions();

            var consoleAppender = new ConsoleAppender {
                Layout = layout
            };
            consoleAppender.ActivateOptions();

            hierarchy.Root.AddAppender(consoleAppender);
            hierarchy.Root.Level = log4net.Core.Level.Info;
            hierarchy.Configured = true;

            var logger = LogManager.GetLogger(typeof(Program));

            // install SimpleW -> log4net bridge
            using var bridge = SimpleWLog4NetBridge.Install(logger, replaceExistingSink: false);

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // minimal api
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            // start a blocking background server
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