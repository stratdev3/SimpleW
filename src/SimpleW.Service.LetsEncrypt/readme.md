# SimpleW.Service.LetsEncrypt

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
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
using SimpleW.Service.LetsEncrypt;

namespace Sample {
    class Program {

        static async Task Main() {

            // start listening to all IPs port 80
            var server = new SimpleWServer(IPAddress.Any, 80);

            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            // once the this module load and certificate created
            // it will listen to 443 and enable ssl
            server.UseLetsEncryptModule(options => {
                options.Email = "letsenc@simplew.net";
                options.Domains = [ "simplew.net", "www.simplew.net" ];
                options.UseStaging = false;
                options.HttpsPort = 443; // default value
            });

            server.OnStarted(s => {
                Console.WriteLine("server started at http://localhost:{server.Port}/");
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