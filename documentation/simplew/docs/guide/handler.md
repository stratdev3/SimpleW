# Handler

## Overview

In SimpleW, a **handler** is the core execution unit that processes an HTTP request and produces a response.
Handlers are designed to be :

- Flexible: sync, async, Task, ValueTask, with or without return values
- Fast: compiled once into an optimized execution pipeline

::: info In short
A handler is just a method, but SimpleW turns it into a full HTTP execution pipeline.
:::


## What is a Handler ?

A handler is a C# `delegate` or a `controller method` that is registered to handle an HTTP request.
From SimpleW’s point of view, a handler *is anything that can be executed by the router to process a request*.
There are two equivalent ways to define a handler :

1. **Delegate-based handlers** (functional style)
2. **Controller methods decorated with Route Attributes** (class-based style)

Both models are internally normalized and executed through the same handler pipeline.


## Delegate-Based Handlers

A delegate-based handler is registered explicitly using [`Map`](../reference/simplewserver.md#map), `MapGet`, `MapPost`.

Example :

```csharp
server.MapGet("/api/hello", static () => {
    return new { message = "Hello world" };
});
```

At runtime:
1. The router matches the request path and HTTP method
2. The associated handler is executed
3. The handler result (if any) is processed and converted into an HTTP response


## Controller Methods Handlers

Methods declared in a class that **inherits from [`Controller`](../reference/controller.md)** and **decorated with a [`[Route]`](../reference/routeattribute.md) attribute** are also handlers.

Example :

```csharp
public class UserController : Controller {

    [Route("GET", "/api/users/:id")]
    public object GetUser(int id) {
        return new { id };
    }

}
```

From a conceptual point of view:

> A controller method with a [Route] attribute is a handler.

Internally :

- The method is discovered via reflection
- A handler executor is generated exactly like for a delegate
- Parameters are bound using the same rules (route, query, session)
- The return value is processed by the same HttpHandlerResult

Controllers **do not introduce a different execution model**. They are *a structured way to declare handlers*, not a separate abstraction.


## Supported Handler Signatures

SimpleW supports a wide range of handler signatures.

### Parameters

A handler can declare parameters that are automatically resolved :

| Parameter source      | Resolution rule                        |
|-----------------------|----------------------------------------|
| `HttpSession`         |   Injected directly                    |
|Route parameters       |   Matched by name (:id, :name, etc.)   |
|Query string           |   Matched by name                      |
|Optional parameters    |   Default value is used if missing     |

Example :

```csharp
server.MapGet("/api/user/:id", (int id, string? filter = null) => {
    return new { id, filter };
});
```

Resolution priority :

1. Route values
2. Query string
3. Default parameter value

If a required parameter is missing, an exception is thrown.


## Return Types

Handlers may return


### No Result

```csharp
server.MapGet("/ping", static () => { });
```

The request is considered handled, no automatic response is sent.

### Synchronous Result

```csharp
server.MapGet("/api/data", () => {
    return new { message = "Hello World !" }
});
```

The returned value is forwarded to the **Handler Result Processor**.

### Async (Task / ValueTask)

```csharp
server.MapGet("/async", async () => {
    await Task.Delay(100);
    return "done";
});
```

Supported forms: `ValueTask`, `ValueTask<T>`, `Task`, `Task<T>`.


## Direct Response Control

A handler may directly manipulate the response :

```csharp
server.MapGet("/raw", (HttpSession session) => {
    return session.Response
                  .Status(200)
                  .Text("Hello");
});
```

If an `HttpResponse` is returned, SimpleW ensures it belongs to the current session.

::: tip NOTE
See the [Response](./response.md) for more examples.
:::


## Handler Result Processing

When a handler returns a non-null value, it is passed to a global HttpHandlerResult delegate.

### Default Behavior

By default :
- The result is serialized as JSON
- The response is sent immediately

Conceptually :
```
handler() -> object result -> HandlerResult -> HttpResponse
```

### Custom Handler Result

You can override this behavior globally :

```csharp
server.ConfigureHandlerResult((session, result) => {
    Console.WriteLine("Processing result");
    return session.Response.Json(result).SendAsync();
});
```

This allows logging, delayed responses, conditional serialization, custom response strategies...

::: tip NOTE
See the [Result Handler](./handlerresult.md) for more examples.
:::


## Middleware and Handlers

Handlers are executed inside a middleware pipeline.

Execution order:
1. First middleware
2. Next middleware
3. ...
4. Final handler
5. Unwind middleware stack

Example middleware :

```csharp
server.UseMiddleware(async (session, next) => {
    Console.WriteLine("Before handler");
    await next();
    Console.WriteLine("After handler");
});
```
From the handler perspective, middleware is transparent.

::: tip NOTE
See the [Middleware](./middleware.md) for more examples.
:::
