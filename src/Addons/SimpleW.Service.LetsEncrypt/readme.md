# SimpleW.Service.LetsEncrypt

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![License](https://img.shields.io/badge/license-MIT-7737d1.svg)](https://github.com/Stratdev3/SimpleW/blob/master/licence)
[![NuGet](https://img.shields.io/nuget/v/SimpleW.Service.LetsEncrypt?color=7737d1&label=version&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.LetsEncrypt)
[![Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.LetsEncrypt?color=7737d1&label=downloads&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.LetsEncrypt)

<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

LetsEncrypt is a SimpleW module that automatically provisions and renews TLS certificates using Let’s Encrypt (ACME).

### Getting Started

The minimal API

```cs
using SimpleW;
using SimpleW.Observability;
using SimpleW.Service.LetsEncrypt;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

            // start listening to all IPs port 80
            var server = new SimpleWServer(IPAddress.Any, 80);

            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            // once this module loads or creates a certificate,
            // it switches to 443 and enables TLS on the selected engine
            server.UseLetsEncryptModule(options => {
                options.Email = "letsenc@simplew.net";
                options.Domains = [ "simplew.net", "www.simplew.net" ];
                options.UseStaging = false;
                options.HttpsPort = 443; // default value

                // Custom engines can override OnEngineHttpsEnable / OnEngineHttpsDisable.
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
