# Templates


The [`SimpleW.Templates`](https://www.nuget.org/packages/SimpleW.Templates) provides a simple and efficient way to scaffold new
SimpleW modules, services, or projects using `dotnet new` templates.

It is designed to :
- Standardize module structure
- Reduce boilerplate
- Enforce SimpleW conventions
- Make it easy to create new extensions consistently

This module does **not** add any runtime dependency to SimpleW.


## Features

It allows you to :
- Generate ready-to-use SimpleW modules
- Create consistent project layouts
- Preconfigure `.csproj` files for SimpleW
- Bootstrap new modules in seconds
- Use `dotnet new` with SimpleW-specific templates


## Installation

Install the templates package using :

```bash
dotnet new install SimpleW.Templates
```

You can browse all the available templates :

```bash
dotnet new list simplew
```


## Available Templates

### simplew-minimal

The `simplew-minimal` template generates a **minimal SimpleW application** with the smallest possible setup.
It's based on the [getting started](../guide/getting-started.md#minimal-example) example.


**Create a project**

```bash
# creates a new folder "MySimpleWApp" with the minimal template
dotnet new simplew-minimal -n MySimpleWApp
cd MySimpleWApp

# run it
dotnet run
```
