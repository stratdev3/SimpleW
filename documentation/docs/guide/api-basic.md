# Basic

Few lines of code to setup a REST API :
- [`SimpleWServer`](../reference/simplewserver) : main class to manage the server (Start, Stop, Add Components)
- [`Controller`](../reference/controller-overview) : base class for your middleware to inherit Request/Response properties
- `Route` : attribut to target method of your middleware
- `Data` : data return by middleware is default serialized to json and sent as reponse

The following code is full of comments :

::: code-group

<<< @/snippets/basic.cs#snippet{csharp:line-numbers} [program.cs]

:::

Then just open your browser to http://localhost:2015/api/test and you will see the `{ "message": "Hello World !" }` json response.

::: tip NOTE
The controller __CAN NOT__ have constructor. All properties [`Request`](../reference/controller-httprequest),  [`Response`](../reference/controller-httpresponse) (...) will be injected after session instanciation. See [callback](./api-callback) on how to inject specific code in all controllers of methods.
:::
