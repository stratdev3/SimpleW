# Response

There are three ways to send data to the client :

1. **Return** : the default fast path, any handler return is serialized and sent.
2. **Response** : fined-grained control with a high level object you can manipulate.
3. **Raw Bytes** : you entirely own the bytes to send.


## Return

The return of any handler is serialized to json and sent.
This is the simplest and fastest way to send a response as SimpleW take care of everything for you.

The following example illustrates the `object` return type :

::: code-group

<<< @/snippets/response-default-object.cs#snippet{20-25 csharp:line-numbers} [program.cs]

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

<<< @/snippets/response-default-any.cs#snippet{28-31,38,48-52 csharp:line-numbers} [program.cs]

:::

::: tip NOTE
There is no need to specify the exact type the method will return.
Most of the time, `object` is enough and will be passed to a `IJsonEngine.Serialize(object)`.
:::


## Response

The `Response` object allows a fine-grained control over the data sent to the client.
`Response` is a property of both `HttpSession` and `Controller`.

The following example illustrates how a custom response can be forged easily :

::: code-group

<<< @/snippets/response-response.cs#snippet{13-21,36-38,43-44,49-50,55 csharp:line-numbers} [program.cs]

:::

::: tip NOTE
See the [HttpResponse](../reference/httpresponse.md) for details of its methods.
:::


### Common Response

Even though it’s easy to build a custom response, it’s also possible to reduce the amount of code for common responses.

The following example illustrates some built-in [response](../reference/httpresponse#aliases).

::: code-group

<<< @/snippets/response-common.cs#snippet{27,40,53,59 csharp:line-numbers} [program.cs]

:::


## Send Raw Bytes

::: warning
The [`Session.SendAsync()`](../reference/httpsession.md#sendasync) method bellow is the lowest level to send data to client. They are barely aliases of `Socket.SendAsync()` with a thread-safe guard.
You should never need to use them and should not use because it breaks the middleware pipeline and other handler.
:::



::: code-group

<<< @/snippets/response-session-sendasync.cs#snippet{20 csharp:line-numbers} [program.cs]

:::
