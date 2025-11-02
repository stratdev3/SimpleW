# Response

There are multiple ways to return data to the client :
1. By **[default](#default)**, the return of method is serialized and automatically sent to the client.
2. However, you can also manipulate the [response](#response-property) property for fine-grained control.
3. You want to [send raw bytes](#send-raw-bytes), that's possible too.


## Default

By default, the return of the method will be serialized to json using the [`JsonEngine`](#json-engine).
It will also use compression _(gzip, br...)_ dependings on the encoding types supported by the client.

The following example illustrates the `object` return type :

::: code-group

<<< @/snippets/response-default-object.cs#snippet{csharp:line-numbers} [program.cs]

:::

A request to `http://localhost:2015/api/test` will result in :

```json
{
    "message": "Hello World !",
    "current": "2024-03-01T13:17:29.1249399+01:00",
    "i": 0,
    "enable": true,
    "d": {"Foo":"Bar"}
}
```

Any return type (`object`, `List`, `Dictionary`, `String`...) will be serialized and sent as json to the client.

The following example illustrates different return types :

::: code-group

<<< @/snippets/response-default-any.cs#snippet{csharp:line-numbers} [program.cs]

:::

::: tip NOTE
There is no need to specify the exact type the method will return.
Most of the time, `object` is enough and will be passed to a `IJsonEngine.Serialize(object)`.
:::


## Response Property

The `Response` property allows a fine-grained control over the data sent to the client.

The following example illustrates how a custom response can be forged easily :

::: code-group

<<< @/snippets/response-response.cs#snippet{csharp:line-numbers} [program.cs]

:::

::: tip NOTE
See the [HttpResponse](../reference/httpresponse.md) for details of its methods.
:::


### Helpers

Even though it’s easy to build a custom response, it’s also possible to reduce the amount of code for common responses.

The following example illustrates some built-in [helpers](../reference/httpresponse#helpers).

::: code-group

<<< @/snippets/response-response-helpers.cs#snippet{csharp:line-numbers} [program.cs]

:::


## Send Raw Bytes

The [`Session.SendResponseBody()`](../reference/isimplewsession.md#sendresponsebody) method is the lower level, and it basically consists of sending bytes to the client.

::: code-group

<<< @/snippets/response-sendresponseasync.cs#snippet{csharp:line-numbers} [program.cs]

:::

::: tip NOTE
These methods should be used carefully for your possible edge case.
:::

## Json Engine

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
