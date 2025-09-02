# CORS


Internet Browser (Firefox, Chrome, IE...) blocks javascript requesting RestAPI from a different domain. That's why [CORS](https://developer.mozilla.org/fr/docs/Web/HTTP/CORS) was created, to define permission and sharing data.

To set CORS policy, use the [`server.AddCORS()`](../reference/simplewserver#cors) method :

::: code-group

<<< @/snippets/api-cors.cs#snippet{csharp:line-numbers} [program.cs]

:::
