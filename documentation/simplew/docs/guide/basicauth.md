# Basic Auth

The **BasicAuthModule** provides lightweight HTTP **Basic Authentication** for
SimpleW applications.

It allows you to protect one or more **URL prefixes** with username/password
credentials, using a **single middleware in the pipeline**, no matter how many
times the module is configured.


## How It Works

Each call to [`SimpleWServer.UseBasicAuthModule()`](../reference/basicauthmodule.md) **adds or updates a rule** for a given URL
prefix.

Internally:
- All rules are stored in a shared registry
- Only **one middleware** is installed per `SimpleWServer`
- Incoming requests are matched against registered prefixes
- The **longest matching prefix** is selected
- Authentication is enforced only for that prefix


## Basic Usage

```csharp
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            server.MapGet('/api/test/hello', () => {
                return new { message = "Hello World !" };
            });

            // set basic auth module
            server.UseBasicAuthModule(options => {
                options.Prefix = "/api/test";
                options.Realm = "Admin";
                options.Users = new[] { new BasicAuthModuleExtension.BasicAuthOptions.BasicUser("chris", "secret") };
            })
            // set another basic auth module
            .UseBasicAuthModule(options => {
                options.Prefix = "/metrics";
                options.Realm = "Metrics";
                options.CredentialValidator = (user, password) => user == "prom" && password == "scrape";
            });

            server.MapControllers<Controller>("/api");

            server.OnStarted(s => {
                Console.WriteLine("server started at http://localhost:{server.Port}/");
            });
            await server.RunAsync();
        }
    }

}
```

::: warning
- You should never expose BasicAuth over plain HTTP, but above HTTPS !
- Credentials are Base64-encoded, not encrypted.
:::