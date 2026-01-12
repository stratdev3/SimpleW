# Getting Started

Stop talking and show me the code !

<div class="images-inline">
    <a href="https://github.com/quozd/awesome-dotnet?tab=readme-ov-file#web-servers" target="_blank"><img src="/public/awesome.svg" alt="dotnet awesome" /></a>
    <a href="https://www.nuget.org/packages/SimpleW" target="_blank"><img src="https://img.shields.io/nuget/dt/SimpleW" alt="NuGet Downloads" /></a>
</div>


## Installation

Using the nuget package, always prefer the last version.

```sh
$ dotnet add package SimpleW --version 26.0.0-alpha.20260112-2345
```

::: tip NOTE
Follow this [guide](./migrate-from-v16) if you migrate from v16.
:::


## Minimal Example

The following minimal example can be used for rapid prototyping :

::: code-group

<<< @/snippets/getting-started-minimal.cs#snippet{csharp:line-numbers} [program.cs]

:::

It uses the [`MapGet`](../reference/simplewserver.md#map) and [`MapPost`](../reference/simplewserver.md#map) methods, which are quite the same as asp.net core.


## Rest API Example

The following example build a REST API with a clear routing and controller/method structured class.

::: code-group

<<< @/snippets/basic-program.cs#snippet{csharp:line-numbers} [Program.cs]

<<< @/snippets/basic-testcontroller.cs#snippet{csharp:line-numbers} [TestController.cs]

:::

Then just [open your browser](http://localhost:2015/api/test?name=Chris) to and you will see the `{ "message": "Hello Chris !" }` JSON response.


## Static Files Example

The following example serve statics files in your `c:\www` directory.

::: code-group

<<< @/snippets/getting-started-frontend.cs#snippet{csharp:line-numbers} [Program.cs]

<<< @/snippets/getting-started-frontend.html#snippet{html:line-numbers} [C:\www\index.html]

:::

Then just [open your browser](http://localhost:2015/).


## FullStack Example

The following example will serve both static files in your `c:\www` directory and a REST API.

::: code-group

<<< @/snippets/getting-started-fullstack-program.cs#snippet{csharp:line-numbers} [Program.cs]

<<< @/snippets/getting-started-fullstack-testcontroller.cs#snippet{csharp:line-numbers} [TestController.cs]

<<< @/snippets/getting-started-fullstack.html#snippet{html:line-numbers} [C:\www\index.html]

:::

Then just Then just [open your browser](http://localhost:2015/) and interact with the form.



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