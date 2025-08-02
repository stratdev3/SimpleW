# Getting Started 

## Installation

Using the [SimpleW](https://www.nuget.org/packages/SimpleW) nuget package, always prefer the last [version](https://github.com/stratdev3/SimpleW/blob/master/release.md).

```sh
$ dotnet add package SimpleW
```
<div class="images-inline">
    <img src="https://img.shields.io/nuget/dt/SimpleW" alt="NuGet Downloads" />
    <img src="https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg" alt="Linux" />
    <img src="https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg" alt="Windows" />
    <img src="https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg" alt="MacOS" />
</div>

<br/>

::: tip NOTE
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
.images-inline img {
    max-width: 30%;
    height: auto;
}
</style>