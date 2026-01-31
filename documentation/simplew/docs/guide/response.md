# Response

SimpleW provides **three distinct and complementary ways** to send data back to the client.
They are ordered from *highest-level / most automatic to lowest-level / most explicit*.

1. **Return values** – the default, fastest, and most idiomatic path
2. **HttpResponse** – fine-grained, explicit control over the response
3. **Raw bytes** – full ownership of the HTTP payload (advanced / escape hatch)


## 1. Return Values (Default Path)

Returning a value from a handler is the simplest and fastest way to produce a response.

### Example: Object Return

```csharp
[Route("GET", "/test")]
public object Test() {
    return new {
        message = "Hello World !",
        current = DateTime.Now,
        i = 0,
        enable = true,
        d = new Dictionary<string, string> { { "Foo", "Bar" } }
    };
}
```

Response:

```json
{
  "message": "Hello World !",
  "current": "2024-03-01T13:17:29.1249399+01:00",
  "i": 0,
  "enable": true,
  "d": { "Foo": "Bar" }
}
```

When a handler returns a non-null value :
- The value is passed to the global `HttpHandlerResult`
- By default, it is serialized as JSON
- The response is sent automatically

You do not need to :
- Create a response object
- Set headers
- Call `SendAsync()`

SimpleW takes care of everything.

### Supported Return Types

Any serializable value can be returned :
- `object`
- `string`
- `array` / `List<T>`
- `Dictionary<TKey, TValue>`
- POCOs

```csharp
return new[] { "hello", "world" };
```

::: tip NOTE
You rarely need to specify an exact return type. Returning object is usually enough and will be serialized using IJsonEngine.Serialize(object).
:::

### Mental Model

> Returning a value means: « here is the data, SimpleW will turn it into an HTTP response ».

This is the **recommended path** for APIs and most endpoints.


## 2. HttpResponse (Fine‑Grained Control)

When you need more control, you can return an explicit `HttpResponse` instance.

`HttpResponse` allows you to :
- Set status codes
- Control headers
- Set cookies
- Choose content type
- Send files
- Control compression

`HttpResponse` is accessible from :
- `HttpSession.Response`
- `Controller.Response`

### Example

```csharp
[Route("GET", "/json")]
public object Json() {
    return Response.Json("<p>Hello, World</p>")
                   .AddHeader("X-trace-id", "1234")
                   .AddHeader("X-custom", "test");
}
```

Another example :

```csharp
[Route("GET", "/plaintext")]
public object Plaintext() {
    return Response.Text("Hello World !")
                   .SetCookie("mycookie", "myvalue", new HttpResponse.CookieOptions(secure: true, maxAgeSeconds: 900));
}
```

### Common Responses (Aliases)

SimpleW provides helpers for frequent cases:

```csharp
return Response.NotFound("Not found");
return Response.Unauthorized("Unauthorized");
return Response.InternalServerError("Error");
return Response.Redirect("https://example.com");
```

These helpers reduce boilerplate while keeping full control.

::: tip NOTE
See [`HttpResponse`](../reference/httpresponse.md) reference documentation for the full API.
:::

### Mental Model

> Returning an HttpResponse means: « I control exactly how this response looks ».

Use this when HTTP semantics matter (status codes, headers, files, cookies).


## 3. Raw Bytes (Lowest Level)

You can bypass `HttpResponse` entirely and send raw bytes directly to the client.

```csharp
[Route("GET", "/raw")]
public async ValueTask Raw() {
    await Session.SendAsync(BuildRawResponse());
}
```
This approach means :
- You manually build the full HTTP response
- You own headers, status line, encoding, and body
- SimpleW does not help you

::: warning
[`Session.SendAsync()`](../reference/httpsession.md#sendasync) is a very low-level primitive :
- It is close to `Socket.SendAsync()`
- It bypasses the middleware pipeline
- It should almost never be used

This is an escape hatch for very specific scenarios only.
:::

### Mental Model

> Sending raw bytes means: « I fully own HTTP, SimpleW steps aside ».


## Choosing the Right Approach

| Use case                          | Recommended approach |
|-----------------------------------|----------------------|
| REST / JSON API                   |   Return values      |
| Custom status / headers           |   HttpResponse       |
| File download                     |   HttpResponse       |
| HTML / assets                     |   StaticFilesModule  |
| Experimental / special protocol   |   Raw bytes          |
