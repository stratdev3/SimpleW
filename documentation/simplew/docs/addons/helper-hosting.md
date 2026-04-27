# Hosting

The [`SimpleW.Helper.Hosting`](https://www.nuget.org/packages/SimpleW.Helper.Hosting) package provides a helper that allows you to run a **SimpleW server** using the **Microsoft.Extensions.Hosting** infrastructure.

It is designed for developers who are used to the .NET *Generic Host* model and want :
- standard application lifecycle management
- configuration via `appsettings.json`, environment variables, and command-line
- clean startup / shutdown handling (SIGTERM, Ctrl+C, systemd, Windows Service)
- zero changes to the SimpleW core server

::: info
**SimpleW itself does NOT depend on Microsoft Hosting.**
This package is only a thin adapter layer.
:::


## Features

It allows you to :
- Start and stop `SimpleWServer` as a hosted service
- Configure SimpleW using the Generic Host (`HostApplicationBuilder`)
- Override the listening URL from configuration or code
- Integrate SimpleW into existing .NET hosting environments


## Requirements

- .NET 8.0
- SimpleW (core server)
- Microsoft.Extensions.Hosting 8.x or higher (automatically included)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.Hosting --version 26.0.0
```


## Minimal example

```csharp
using Microsoft.Extensions.Hosting;
using SimpleW;
using SimpleW.Helper.Hosting;

var builder = SimpleWHost.CreateApplicationBuilder(args);

builder.ConfigureSimpleW(server => {
    server.MapGet("/hello", () => {
        return new { message = "Hello World !" };
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
        return new { message = "Hello World !" };
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

::: info
Unlike Kestrel, SimpleW can listen on only one port at a time. To listen on another port, just create a new instance of SimpleW.
:::

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

When you need access to the built `IServiceProvider` during server configuration, use the overload below.
This is especially useful for packages that plug into `SimpleWServer` before routes are mapped.

```csharp
builder.ConfigureSimpleW(
    configureApp: (services, server) => {
        // configure SimpleW with access to the host service provider
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


## Bridging SimpleW logging with Microsoft.Extensions.Logging

When using `SimpleW.Helper.Hosting`, you can forward the [**SimpleW internal logger**](../guide/logging.md) to the **Microsoft.Extensions.Logging** pipeline.

This allows SimpleW logs to be handled by the same logging providers used by the Generic Host (console, Serilog, OpenTelemetry, etc.).

To enable it, call the extension method `UseMicrosoftLogging()` on the `SimpleWHostApplicationBuilder`.

```csharp
using Microsoft.Extensions.Hosting;
using SimpleW;
using SimpleW.Helper.Hosting;

var builder = SimpleWHost.CreateApplicationBuilder(args)
                         .UseMicrosoftLogging();

builder.ConfigureSimpleW(server => {
    server.MapGet("/", () => {
        return new { message = "Hello world" };
    });
});

var host = builder.Build();
await host.RunAsync();
```

### What this does

The bridge forwards every log produced by the SimpleW logging system to the `Microsoft.Extensions.Logging` infrastructure.

This means :
- SimpleW logs appear in the **same output** as your application logs
- All configured logging providers are used automatically
- The **minimum log level configured in Microsoft logging is respected**

For example, with the default host configuration, logs will appear in the console :

```
info: SimpleW.Server[0]
      Listening on http://0.0.0.0:8080
```



## Design philosophy

- **SimpleW stays minimal and hosting-agnostic**
- This package is a _thin adapter_, nothing more
- No ASP.NET Core dependency
- No forced dependency injection model
- No hidden magic

If you do not need Microsoft Hosting, **do not use this package**.
You can continue to instantiate and run `SimpleWServer` directly.
