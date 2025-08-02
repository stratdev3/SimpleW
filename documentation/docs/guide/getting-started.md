# Getting Started 

## Installation

Using the [SimpleW](https://www.nuget.org/packages/SimpleW) nuget package, always prefer the last [version](https://github.com/stratdev3/SimpleW/blob/master/release.md).

```sh
$ dotnet add package SimpleW
```
<div class="images-inline">
    <a href="https://github.com/quozd/awesome-dotnet?tab=readme-ov-file#web-servers" target="_blank"><img src="https://camo.githubusercontent.com/8693bde04030b1670d5097703441005eba34240c32d1df1eb82a5f0d6716518e/68747470733a2f2f63646e2e7261776769742e636f6d2f73696e647265736f726875732f617765736f6d652f643733303566333864323966656437386661383536353265336136336531353464643865383832392f6d656469612f62616467652e737667" alt="dotnet awesome" /></a>
    <a href="https://www.nuget.org/packages/SimpleW" target="_blank"><img src="https://img.shields.io/nuget/dt/SimpleW" alt="NuGet Downloads" /></a>
    <img src="https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg" alt="Linux" />
    <img src="https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg" alt="Windows" />
    <img src="https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg" alt="MacOS" />
</div>

<br/>

::: details NOTE
SimpleW depends on [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) package for json serialization/deserialization.
It will be replaced in futur by the native `System.Text.Json` as long as
some advanced features will be covered (`Populate` and `streamingContextObject`, see [work-in-progress](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/migrate-from-newtonsoft?pivots=dotnet-8-0#table-of-differences)).
:::


## REST API Example (backend)

The following example build a REST API with a clear routing and controller/method structured class.

::: code-group

<<< @/snippets/getting-started-backend.cs#snippet{csharp:line-numbers} [program.cs]

:::

Then just open your browser to http://localhost:2015/api/test/hello?name=Chris and you will see the `{ "message": "Chris, Hello World !" }` json response.


## Static Files Example (frontend)

The following example serve statics files in your `c:\www` directory.

::: code-group

<<< @/snippets/getting-started-frontend.cs#snippet{csharp:line-numbers} [program.cs]

<<< @/snippets/getting-started-frontend.html#snippet{html:line-numbers} [C:\www\index.html]

:::

Then just point your browser to http://localhost:2015/.


## FullStack Example (backend+frontend)

The following example will serve both static files in your `c:\www` directory and a REST API.

::: code-group

<<< @/snippets/getting-started-fullstack.cs#snippet{csharp:line-numbers} [program.cs]

<<< @/snippets/getting-started-fullstack.html#snippet{html:line-numbers} [C:\www\index.html]

:::


Then just point your browser to http://localhost:2015/ and interact with the form.

<style>
.images-inline {
    display: flex;
    gap: 1rem;
    align-items: center;
    flex-wrap: wrap;
}
.images-inline > img {
    max-width: 30% !important;
    height: auto !important;
}
</style>