# Getting Started 

Stop talking and show me the code !

## Installation

Using the [SimpleW](https://www.nuget.org/packages/SimpleW) nuget package.

```sh
$ dotnet add package SimpleW --version 16.1.0
```
<div class="images-inline">
    <a href="https://github.com/quozd/awesome-dotnet?tab=readme-ov-file#web-servers" target="_blank"><img src="/public/awesome.svg" alt="dotnet awesome" /></a>
    <a href="https://www.nuget.org/packages/SimpleW" target="_blank"><img src="https://img.shields.io/nuget/dt/SimpleW" alt="NuGet Downloads" /></a>
</div>


## Minimal Example

The following minimal example can be used for rapid prototyping :

::: code-group

<<< @/snippets/getting-started-minimal.cs#snippet{csharp:line-numbers} [program.cs]

:::

::: tip NOTE
While this example is perfect for rapid prototyping, it lacks proper organization.
Take a look at the [basic](./api-basic.md) of organizing your code following the Controller pattern.
:::


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