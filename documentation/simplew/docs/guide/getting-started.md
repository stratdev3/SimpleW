# Getting Started 

Stop talking and show me the code !

## Installation

Using the [SimpleW](https://www.nuget.org/packages/SimpleW) nuget package, always prefer the last [version](https://github.com/stratdev3/SimpleW/blob/master/release.md).

```sh
$ dotnet add package SimpleW --version 26.0.0-alpha.20251221-1244
```
<div class="images-inline">
    <a href="https://github.com/quozd/awesome-dotnet?tab=readme-ov-file#web-servers" target="_blank"><img src="https://camo.githubusercontent.com/8693bde04030b1670d5097703441005eba34240c32d1df1eb82a5f0d6716518e/68747470733a2f2f63646e2e7261776769742e636f6d2f73696e647265736f726875732f617765736f6d652f643733303566333864323966656437386661383536353265336136336531353464643865383832392f6d656469612f62616467652e737667" alt="dotnet awesome" /></a>
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