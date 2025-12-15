# Basic

SimpleW covers the most common web server use cases — from minimal code setups to full-stack applications.

## Minimal Example

The following minimal example can be used for rapid prototyping.

::: code-group

<<< @/snippets/getting-started-minimal.cs#snippet{csharp:line-numbers} [program.cs]

:::

It uses the [`MapGet`](../reference/simplewserver.md#inline-func) and [`MapPost`](../reference/simplewserver.md#inline-func) methods, which are quite the same as asp.net core.

## Rest API Example

The following example build a REST API with a clear routing and controller/method structured class.

::: code-group

<<< @/snippets/basic-program.cs#snippet{csharp:line-numbers} [Program.cs]

<<< @/snippets/basic-testcontroller.cs#snippet{csharp:line-numbers} [TestController.cs]

:::

Then just open your browser to <http://localhost:2015/api/test/hello?name=Chris> and you will see the `{ "message": "Hello Chris !" }` JSON response.

::: tip NOTE
The controller __CAN NOT__ have constructor. All properties [`Request`](../reference/httprequest), [`Response`](../reference/httpresponse) (...) will be injected after session instanciation. See [callback](./api-callback) on how to inject specific code in controllers.
:::


## Static Files Example

The following example serve statics files in your `c:\www` directory.

::: code-group

<<< @/snippets/getting-started-frontend.cs#snippet{csharp:line-numbers} [Program.cs]

<<< @/snippets/getting-started-frontend.html#snippet{html:line-numbers} [C:\www\index.html]

:::

Then just point your browser to http://localhost:2015/.


## FullStack Example

The following example will serve both static files in your `c:\www` directory and a REST API.

::: code-group

<<< @/snippets/getting-started-fullstack-program.cs#snippet{csharp:line-numbers} [Program.cs]

<<< @/snippets/getting-started-fullstack-testcontroller.cs#snippet{csharp:line-numbers} [TestController.cs]

<<< @/snippets/getting-started-fullstack.html#snippet{html:line-numbers} [C:\www\index.html]

:::

Then just point your browser to http://localhost:2015/ and interact with the form.

::: tip NOTE
You frontend can be anything, you can serve a vue/react/whatever javascript application you want.
In fact, for [my needs](./what-is-simplew.md#my-needs), I have to serve a frontend [vuetify](https://vuetifyjs.com) application.

See the [How to serve a vue.js app](./static-files.md#how-to-serve-a-vue-app) with SimpleW.
:::
