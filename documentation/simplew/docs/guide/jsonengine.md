# Json Engine

This [`JsonEngine`](../reference/simplewserver#jsonengine) property defines the Json engine used in server and controllers to serialize, deserialize and populate objects.
The default engine is `System.Text.Json` initialized with recommanded options.

There is an additionnal [SimpleW.Newtonsoft](https://www.nuget.org/packages/SimpleW.Newtonsoft) nuget package which provide an alternative Json engine, the awesome [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json).

To change the Json Engine for Newtonsoft

```sh
$ dotnet add package SimpleW.Newtonsoft
```

And then

::: code-group

<<< @/snippets/json-engine.cs#snippet{13-30 csharp:line-numbers} [program.cs]

:::
::: tip NOTE

You can create your own JsonEngine by implementing the [`IJsonEngine`](../reference/ijsonengine.md) interface.

:::
