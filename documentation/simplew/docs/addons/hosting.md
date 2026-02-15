# Hosting

[`SimpleW.Helper.Hosting`](https://www.nuget.org/packages/SimpleW.Helper.Hosting) is an optional integration package that allows you to run a **SimpleW server** using the **Microsoft.Extensions.Hosting** infrastructure.

It is designed for developers who are used to the .NET *Generic Host* model and want :
- standard application lifecycle management
- configuration via `appsettings.json`, environment variables, and command-line
- clean startup / shutdown handling (SIGTERM, Ctrl+C, systemd, Windows Service)
- zero changes to the SimpleW core server

::: warning
**SimpleW itself does NOT depend on Microsoft Hosting.**
This package is only a thin adapter layer.
:::


## What this package does

`SimpleW.Helper.Hosting` allows you to :
- Start and stop `SimpleWServer` as a hosted service
- Configure SimpleW using the Generic Host (`HostApplicationBuilder`)
- Override the listening URL from configuration or code
- Integrate SimpleW into existing .NET hosting environments

It **does not** :
- add ASP.NET Core
- impose any DI model on SimpleW
- change how SimpleW routes or handles requests


## Requirements

- .NET 8.0
- SimpleW (core server)
- Microsoft.Extensions.Hosting (8.x)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.Hosting --version 26.0.0-beta.20260215-1450
```

This package targets .NET 8 and depends on :

```xml
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />

```

Higher versions (9.x, 10.x) are supported automatically through NuGet resolution.


## Basic usage

### Minimal Program.cs

```csharp
using Microsoft.Extensions.Hosting;
using SimpleW;
using SimpleW.Helper.Hosting;

var builder = SimpleWHost.CreateApplicationBuilder(args);

builder.ConfigureSimpleW(server => {
    server.MapGet("/hello", () => {
        return new { mesage = "Hello World !" };
    });
});

var host = builder.Build();
await host.RunAsync();
```

This will :
- create a Generic Host
- start `SimpleWServer` when the host starts
- stop `SimpleWServer` cleanly when the host shuts down


## Configuring the listening URL

Using `appsettings.json`

```json
{
  "SimpleW": {
    "Url": "http://0.0.0.0:8080"
  }
}
```

The URL is parsed and converted to an `IPEndPoint`.

> Currently, only the `http://` scheme is supported in configuration and `UseUrl()`.
> This is intentional and **does not limit HTTPS support**.


## Enabling HTTPS

HTTPS is configured directly on the SimpleWServer, using its native HTTPS support.

To listen on HTTPS :
1. Keep the URL configuration (`http://`) to define the IP and port
2. Enable HTTPS on the server during configuration with [`UseHttps()`](../guide/ssl-certificate.md)

```csharp{2}
builder.ConfigureSimpleW(server => {
    server.UseHttps(); // native SimpleW HTTPS support, see documentation

    server.MapGet("/hello", () => {
        return new { mesage = "Hello World !" };
    });
});
```

### Why HTTPS is configured this way

`SimpleW.Helper.Hosting` is intentionally limited to :
- endpoint binding
- application lifecycle
- configuration loading

Transport security (TLS) is considered a **server-level concern**, not a hosting concern.


## Overriding the URL in code

You can override the configured URL directly from C# :

```csharp
builder.UseUrl("http://127.0.0.1:9090");
```

Priority order 
1. `UseUrl()` (code)
2. `SimpleW:Url` (configuration)
3. Default value (if provided)


## Configuring the SimpleW server

You can configure both :
- the **application** (routes, modules)
- the **server options** (socket behavior, low-level tuning)

```csharp
builder.ConfigureSimpleW(
    configureApp: server => {
        // routes, modules, static files, etc.
    },
    configureServer: options => {
        options.TcpNoDelay = true;
        options.ReuseAddress = true;
    }
);
```


## Hosting features you automatically get

By using `Microsoft.Extensions.Hosting`, you automatically gain :
- Graceful shutdown (SIGTERM, Ctrl+C)
- `systemd` compatibility on Linux
- Windows Service compatibility
- Standard logging infrastructure
- Unified configuration (JSON, env vars, CLI)

Example (Linux service) :

```csharp
Host.CreateApplicationBuilder(args)
    .UseSystemd();
```

No changes are required in SimpleW itself.



## Design philosophy

- **SimpleW stays minimal and hosting-agnostic**
- This package is a _thin adapter_, nothing more
- No ASP.NET Core dependency
- No forced dependency injection model
- No hidden magic

If you do not need Microsoft Hosting, **do not use this package**.
You can continue to instantiate and run `SimpleWServer` directly.
