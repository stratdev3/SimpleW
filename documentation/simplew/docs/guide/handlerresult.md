# Handler Result

This method change the default Handler executed when a **Result is return** from a `Map()` and a `Controller`.
The default handler is `SendJsonResult` which will serialize the result and send it into json.

Example of **Return result** :

```csharp
// example 1
server.MapGet("/", () => {
    // return from a Map
    return new { message = "Hello World !" };
});

// example 2
public class SomeController : Controller {
    [Route("GET", "/test")]
    public object SomePublicMethod(string name = "World") {
        // return from a controller
        return new { message = "Hello World !" };
    }
}
```

If you ever wonder why those return where automaticaly serialized and sent,
this is because the the `ConfigureHandlerResult()`.


### Example

If you want to do something else, you can simply override this behaviour :

```csharp
server.ConfigureHandlerResult(async (session, result) => {
    // wait
    await Task.Delay(2_000);
    // log
    Console.WriteLine("running the handler result");
    // send custom
    await session.Response
                 .AddHeader("custom", "value") // add custom header
                 .Json(result) // serialize to json
                 .SendAsync();
});
```

Browse the `HttpHandlerResults` class to see some of already coded handler.
