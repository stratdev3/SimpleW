# SimpleW.Service.Firewall

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://stratdev3.github.io/SimpleW)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

This package provides IP-based filtering, rate limiting, connection rules and security policies to protect your server from unwanted or abusive traffic for SimpleW.

### Getting Started

The minimal API

```cs
using SimpleW;
using SimpleW.Service.Firewall;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set Newtonsoft.json as a JsonEngine
            server.ConfigureJsonEngine(new NewtonsoftJsonEngine());

            // minimal api
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });
            server.MapGet("/api/admin", () => {
                return new { message = "Administration" };
            });

            // firewall
            server.UseFirewallModule(fw => {

                // global allowlist => default deny
                fw.AllowRules.Add(IpRule.Cidr("192.168.0.0/16"));
                fw.AllowRules.Add(IpRule.Cidr("10.0.0.0/8"));

                // global rate limit
                fw.GlobalRateLimit = new RateLimitOptions {
                    Limit = 200,
                    Window = TimeSpan.FromSeconds(10),
                    SlidingWindow = false
                };

                fw.PathRules.Add(new PathRule {
                    Prefix = "/api/admin",
                    RateLimit = new RateLimitOptions { Limit = 20, Window = TimeSpan.FromSeconds(10) }
                });

            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

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