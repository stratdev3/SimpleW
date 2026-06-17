# Liquid Pages

## Features
LiquidPages is an open-source C# library that brings a Razor Pages–style MVVM framework to Liquid templates. This SimpleW companion package provides a module for integrating LiquidPages into your SimpleW webserver.
For more documentation on how LiquidPages works, check out the documentation [here](https://www.kinetq.com/docs/open-source-software/liquid-pages).

## Requirements

- .NET 9.0
- SimpleW (core server)
- LiquidPages

No external dependencies.

## Installation

```powershell
dotnet add package Kinetq.LiquidPages.SimpleW
```

## Configuration options

| Option name | Default value | Description |
|------------|---------------|-------------|
| MapFallback404 | `false` | Enables or disables the automatic fall back to LiquidPages for 404 errors. |

## Minimal example

Resolve `ILiquidRoutesManager`, `ILiquidResponseMiddleware`, and `ILiquidStartup` from your container, then register page models and file providers before attaching `LiquidPagesModule`:

```csharp
using Kinetq.LiquidPages.Helpers;
using Kinetq.LiquidPages.Interfaces;
using Microsoft.Extensions.FileProviders;
using SimpleW;

var liquidRoutesManager = serviceProvider.GetRequiredService<ILiquidRoutesManager>();
var liquidResponseMiddleware = serviceProvider.GetRequiredService<ILiquidResponseMiddleware>();
var liquidStartup = serviceProvider.GetRequiredService<ILiquidStartup>();

await liquidStartup.RegisterPageModels();
await liquidStartup.RegisterFilters();
liquidStartup.RegisterFileProvider("/", new PhysicalFileProvider(Directory.GetCurrentDirectory()));

var server = new SimpleWServer(IPAddress.Any, 2015);
server.UseModule(new LiquidPagesModule(liquidRoutesManager, liquidResponseMiddleware)
{
    MapFallback404 = true
});

await server.RunAsync();
```
