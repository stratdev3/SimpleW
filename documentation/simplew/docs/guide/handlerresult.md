# Handler Result

Lets start with this basic example.

A `handler` which **return an object** is defined to the route "/api/test".
If you open http://localhost:2015/api/test, you will see that the object has been serialized and sent to json.

```csharp:line-numbers
// listen to all IPs port 2015
var server = new SimpleWServer(IPAddress.Any, 2015);

// minimal api
server.MapGet("/api/test", () => {
    return new { message = "Hello World !" };
});

// start a blocking background server
await server.RunAsync();
```

**Do you ever wonder how that behavior was setup ?** So with the HandlerResult !

## Default HandlerResult

If you look at the source code you will see something like that :

```csharp:line-numbers
/// <summary>
/// Action to do on the non null Result of any handler (Delegate).
/// </summary>
public HttpHandlerResult HandlerResult { get; set; } = HttpHandlerResults.SendJsonResult;

/// <summary>
/// Examples of HttpHandlerResult
/// </summary>
public static class HttpHandlerResults {

    /// <summary>
    /// Send Result as Json
    /// </summary>
    public static readonly HttpHandlerResult SendJsonResult = (session, result) => {
        if (result is HttpResponse response) {
            // must be sure the response return result is the one of the current session !
            if (!ReferenceEquals(response, session.Response)) {
                throw new InvalidOperationException("Returned HttpResponse is not session.Response");
            }
            return response.SendAsync();
        }
        // fallback
        return session.Response.Json(result).SendAsync();
    };

}

```

The `HandlerResult` is defined by `HttpHandlerResults.SendJsonResult` which obviously serialize the result and send data to client.
You can browse the `HttpHandlerResults` class, you will some others `HandlerResults`.


## Configure HandlerResult

You can use the [`SimpleWServer.ConfigureHandlerResult()`](../reference/simplewserver.md#configurehandlerresult) to define how you want to do of the result of handler.

```csharp
server.ConfigureHandlerResult(async (session, result) => {
    // wait 2sec
    await Task.Delay(2_000);
    // log (do not console.writline to production it's a performance killer)
    Console.WriteLine("running the handler result");
    // send custom
    await session.Response
                 .AddHeader("custom", "value") // add custom header
                 .Json(result) // serialize to json
                 .SendAsync();
});
```
