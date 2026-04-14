# SimpleW.Service.BasicAuth

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Service.BasicAuth)](https://www.nuget.org/packages/SimpleW.Service.BasicAuth)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.BasicAuth)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

`SimpleW.Service.BasicAuth` is the convenience module built on top of `SimpleW.Helper.BasicAuth`.

It lets you protect one or more path prefixes with:
- a Basic challenge realm
- optional `OPTIONS` bypass for preflight requests
- inline users/validator configuration, or a pre-built `BasicAuthHelper`

### Getting Started

Minimal module usage:

```cs
using SimpleW;
using SimpleW.Helper.BasicAuth;
using SimpleW.Service.BasicAuth;

namespace Sample {
    class Program {

        static async Task Main() {

            var server = new SimpleWServer(System.Net.IPAddress.Any, 2015);

            server.UseBasicAuthModule(options => {
                options.Prefix = "/admin";
                options.Realm = "Admin Area";
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
            });

            server.MapGet("/admin/me", (HttpSession session) => {
                return new {
                    message = $"Hello {Principal.Name}';
                };
            });

            await server.RunAsync();
        }
    }

}
```

You can register multiple prefix rules by calling `UseBasicAuthModule(...)` several times.

If you want attribute-driven or fully custom auth middleware, instantiate `BasicAuthHelper` directly from `SimpleW.Helper.BasicAuth` and wire the policy yourself.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
