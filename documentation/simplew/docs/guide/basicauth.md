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
using SimpleW.Observability;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // debug log
            Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

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

            await server.RunAsync();
        }
    }

}
```

::: warning
- You should never expose BasicAuth over plain HTTP, always use HTTPS!
- Credentials are Base64-encoded, not encrypted.
:::


## Authentication Result (HttpPrincipal)

When a request is successfully authenticated, the module **automatically assigns a** [`HttpPrincipal`](../guide/principal.md) **to the current session**.

This means that downstream handlers, controllers, and middleware can access the authenticated identity through :

```csharp
if (session.Principal?.Identity?.IsAuthenticated == true) {
    // authenticated
}
```

The principal is available for the entire request lifecycle.


## Default Principal behavior

If no custom configuration is provided, BasicAuth creates a default identity :

```csharp
new HttpPrincipal(new HttpIdentity(
    isAuthenticated: true,
    authenticationType: "Basic",
    identifier: username,
    name: username,
    email: null,
    roles: Array.Empty<string>(),
    properties: [
        new IdentityProperty("login", username),
        new IdentityProperty("auth_scheme", "Basic"),
        new IdentityProperty("realm", realm)
    ]
));
```

This ensures :
- The user is always authenticated when credentials are valid
- A consistent identity model across the framework
- No `null` principal in handlers


## Custom Principal mapping (PrincipalFactory)

You can fully control how the authenticated identity is built by providing a `PrincipalFactory`.

Example

```csharp
server.UseBasicAuthModule(options => {
    options.Prefix = "/admin";

    options.PrincipalFactory = basicAuthContext => {
        var identity = new HttpIdentity(
            isAuthenticated: true,
            authenticationType: "Basic",
            identifier: basicAuthContext.Username,
            name: basicAuthContext.Username,
            email: null,
            roles: new[] { "admin" },
            properties: [
                new IdentityProperty("login", basicAuthContext.Username),
                new IdentityProperty("realm", basicAuthContext.Realm)
            ]
        );

        return new HttpPrincipal(identity);
    };
});
```

## BasicAuthContext

The factory receives a context object containing all authentication data:

```csharp
public sealed class BasicAuthContext {
    public string Username { get; init; }
    public string Password { get; init; }
    public string Prefix { get; init; }
    public string Realm { get; init; }
    public HttpSession Session { get; init; }
}
```

This allows you to :
- Assign roles dynamically
- Implement multi-tenant logic
- Add custom identity properties
- Integrate with external systems
