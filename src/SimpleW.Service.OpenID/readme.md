# SimpleW.Service.OpenID

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

OpenID Connect authentication module for the SimpleW web server with cookie-based sessions.

### Getting Started

The minimal API

```cs
using SimpleW;
using SimpleW.Service.OpenID;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            server.UseOpenIDModule(options => {

                options.Add("google", o => {
                    o.Authority = "https://accounts.google.com";
                    o.ClientId = "azerty";
                    o.ClientSecret = "GOCSPX-azerty";
                    o.PublicBaseUrl = "http://myapp.example.test";
                });

                options.CookieSecure = false;
            });

            // route to protect
            server.Router.MapGet("/api/me", (HttpSession session) => {
                if (!session.Request.User.Identity) {
                    return session.Response
                                  .Unauthorized()
                                  .SendAsync();
                }
                var u = session.Request.User;
                return session.Response.Json(new {
                    authenticated = true,
                    id = u.Id,
                    login = u.Login,
                    mail = u.Mail,
                    fullName = u.FullName,
                    roles = u.Roles,
                    profile = u.Profile
                }).SendAsync();
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