# SimpleW.Helper.BasicAuth

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Helper.BasicAuth)](https://www.nuget.org/packages/SimpleW.Helper.BasicAuth)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Helper.BasicAuth)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

`SimpleW.Helper.BasicAuth` is the reusable HTTP Basic auth engine for SimpleW.

It lets you:
- parse `Authorization: Basic ...`
- validate username/password pairs
- build a `HttpPrincipal`
- send a `401` Basic challenge response

This package does not decide which route must be protected. That policy stays in your middleware.

### Getting Started

Minimal helper usage:

```cs
using System;
using SimpleW;
using SimpleW.Helper.BasicAuth;

namespace Sample {
    class Program {

        static async Task Main() {

            var server = new SimpleWServer(System.Net.IPAddress.Any, 2015);

            BasicAuthHelper basic = new(options => {
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
            });

            server.UseMiddleware(async (session, next) => {

                // restore the basicauth principal
                if (basic.TryAuthenticate(session, out HttpPrincipal principal)) {
                    session.Principal = principal;
                }

                // fast path
                if (session.Metadata.Has<AllowAnonymousAttribute>()) {
                    await next().ConfigureAwait(false);
                    return;
                }

                // check principal
                BasicAuthAttribute? auth = session.Metadata.Get<BasicAuthAttribute>();
                if (auth != null && !session.Principal.IsAuthenticated) {
                    await basic.SendChallengeAsync(session, auth.Realm).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });

            server.MapControllers<Controller>("/api");

            await server.RunAsync();
        }
    }

    [Route("/admin")]
    [BasicAuth("Admin Area")]
    public class AdminController : Controller {

        [Route("GET", "/me")]
        public object Me() {
            return new {
                message = $"Hello {Principal.Name}';
            };
        }

        [AllowAnonymous]
        [Route("GET", "/health")]
        public object Health() {
            return new { ok = true };
        }

    }

    [Route("/home")]
    public class HomeController : Controller {

        [BasicAuth("Home Area")]
        [Route("GET", "/me")]
        public object Me() {
            return new {
                message = $"Hello {Principal.Name}';
            };
        }

        [Route("GET", "/index")]
        public object Index() {
            return new { ok = true };
        }

    }


    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class BasicAuthAttribute : Attribute, IHandlerMetadata {

        public BasicAuthAttribute(string realm = "Restricted") {
            Realm = realm;
        }

        public string Realm { get; }

    }
}
```

If you want a ready-to-use prefix-based module instead of custom middleware, use `SimpleW.Service.BasicAuth`.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
