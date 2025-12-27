# Handler Result

When a [`handler`](./handler.md) returns a non-null value, SimpleW needs to decide **what to do** with that value.

This responsibility is handled by a single, explicit mechanism: `HandlerResult`.

Conceptually :

> HandlerResult defines how handler return values are transformed into an HTTP response.


## The Default Behavior

Consider this minimal example :

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);

server.MapGet("/api/test", () => {
    return new { message = "Hello World !" };
});

await server.RunAsync();
```

When you open http://localhost:2015/api/test, the returned object is :
-  Serialized as JSON
- Written to the response body
- Sent to the client

No explicit response handling code is required.
This behavior is not magic — it is defined by the **default** `HandlerResult`.


## What Is a HandlerResult ?

A `HandlerResult` is a delegate with the following signature :

```csharp
ValueTask HttpHandlerResult(HttpSession session, object result)
```

It receives :
- The current HttpSession
- The value returned by the handler

Its job is to **turn that value into an HTTP response**.


## Default HandlerResult Implementation

By default, SimpleW uses `HttpHandlerResults.SendJsonResult`:

```csharp
public HttpHandlerResult HandlerResult { get; set; } = HttpHandlerResults.SendJsonResult;
```

Simplified behavior :
- If the result is an HttpResponse, it is sent directly
- Otherwise, the result is serialized as JSON and sent

This explains why returning :

```csharp
return new { message = "Hello" };
```

"just works".

### Safety Check

The default implementation also validates that a returned `HttpResponse` belongs to the current session.
This prevents subtle bugs caused by mixing responses across sessions.

## Why HandlerResult Exists

`HandlerResult` provides a **single global interception point for handler outputs**.

This allows you to :
- Centralize response formatting
- Add logging or metrics
- Delay responses
- Apply cross-cutting response logic

All without modifying individual handlers.

## Custom HandlerResult

You can override the default behavior using [`SimpleWServer.ConfigureHandlerResult()`](../reference/simplewserver.md#configurehandlerresult) :

```csharp
server.ConfigureHandlerResult(async (session, result) => {
    // simulate async work
    await Task.Delay(2000);

    // custom logic
    Console.WriteLine("HandlerResult executed");

    await session.Response
                 .AddHeader("custom", "value")
                 .Json(result)
                 .SendAsync();
});
```

This custom `HandlerResult` will be executed for every non-null handler return value.

## Interaction with Handlers

Important rules :
- If a handler returns `null`, `HandlerResult` is not invoked
- If a handler sends the response itself, it should return `null`
- If a handler returns a value, `HandlerResult` becomes responsible for sending the response

### Mental Model

> Handlers produce values. HandlerResult decides how those values leave the server.


## HandlerResult vs Middleware

Although both can affect responses, their roles differ :

| Mechanism	        |   Scope                   |   Purpose                         |
|-------------------|---------------------------|-----------------------------------|
| Middleware	    |   Before / after handler  |   Cross-cutting request logic     |
| HandlerResult	    |   After handler           |   Output processing               |

Use `HandlerResult` for response-level policies, not request filtering.


## Common Use Cases

Typical reasons to customize `HandlerResult` :
- Enforcing a unified API envelope
- Adding global response headers
- Logging serialized responses
- Measuring serialization or send time

