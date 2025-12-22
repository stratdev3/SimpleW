# HttpResponse

This class is used to build a response which will be sent to the client.

As already said in the [guide](../guide/api-response#response-property), a `Response` can be returned by a `Controller` method
and it will be sent async to the client.

This class exposes a **fluent API**, which means you can chain all its methods :

```csharp:line-numbers
Response.Status(200)
        .AddHeader("key", "value")
        .Text("Hello World");
```


## Constructor

```csharp
/// <summary>
/// Constructor
/// </summary>
/// <param name="session"></param>
/// <param name="bufferPool"></param>
public HttpResponse(HttpSession session, ArrayPool<byte> bufferPool)
```

::: danger
Never instanciate your own `HttpResponse`, this is already done by [`HttpSession`](./httpsession.md#response) is a proper way ([GC](https://en.wikipedia.org/wiki/Garbage_collection_(computer_science)) friendly).
:::


## Status

```csharp
/// <summary>
/// Set Status
/// </summary>
/// <param name="statusCode">default: 200</param>
/// <param name="statusText">default: "OK"</param>
/// <returns></returns>
public HttpResponse Status(int statusCode, string? statusText = null)
```


## ContentType

```csharp
/// <summary>
/// Set ContentType
/// </summary>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse ContentType(string contentType)
```


## ContextTypeFromExtension

```csharp
/// <summary>
/// Set ContentType from a file extension (e.g: ".html")
/// </summary>
/// <param name="extension"></param>
/// <returns></returns>
public HttpResponse ContextTypeFromExtension(string extension)
```


## AddHeader

```csharp
/// <summary>
/// Add Header
/// </summary>
/// <param name="name"></param>
/// <param name="value"></param>
/// <returns></returns>
public HttpResponse AddHeader(string name, string value)
```


## Body

```csharp
/// <summary>
/// Set Body from byte[] (borrowed stable, array-backed)
/// </summary>
/// <param name="body"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse Body(byte[] body, string? contentType = "application/octet-stream")
```

```csharp
/// <summary>
/// Set Body from ArraySegment (borrowed stable, array-backed, supports offset/len)
/// </summary>
/// <param name="body"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse Body(ArraySegment<byte> body, string? contentType = "application/octet-stream")
```

```csharp
/// <summary>
/// Set Body from ReadOnlyMemory (borrowed stable if no owner provided)
/// If array-backed => store as Segment immediately
/// If not array-backed => store as Memory
/// </summary>
/// <param name="body"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse Body(ReadOnlyMemory<byte> body, string? contentType = "application/octet-stream")
```

```csharp
/// <summary>
/// Set Body with explicit owner (zero-copy + safe lifetime)
/// </summary>
/// <param name="body"></param>
/// <param name="owner"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse Body(ReadOnlyMemory<byte> body, IDisposable owner, string? contentType = "application/octet-stream")
```


## Text

```csharp
/// <summary>
/// Set UTF-8 text Body (owned pooled buffer)
/// </summary>
/// <param name="body"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse Text(string body, string contentType = "text/plain; charset=utf-8")
```


## Json

```csharp
/// <summary>
/// Set JSON Body serialized into pooled buffer
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="value"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse Json<T>(T value, string contentType = "application/json; charset=utf-8")
```


## File

```csharp
/// <summary>
/// Set File Body
/// </summary>
/// <param name="path"></param>
/// <param name="contentType"></param>
/// <returns></returns>
public HttpResponse File(string path, string? contentType = null)
```

```csharp
/// <summary>
/// Set File Body from FileInfo (avoids re-stat / allows caller to pre-validate)
/// </summary>
/// <param name="fi"></param>
/// <param name="contentType"></param>
/// <returns></returns>
/// <exception cref="ArgumentNullException"></exception>
public HttpResponse File(FileInfo fi, string? contentType = null)
```


## Compression

```csharp
/// <summary>
/// Configure compression policy for this response
/// </summary>
/// <param name="mode"></param>
/// <param name="minSize"></param>
/// <param name="level"></param>
/// <returns></returns>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public HttpResponse Compression(ResponseCompressionMode mode, int? minSize = null, CompressionLevel? level = null)
```


## NoCompression

```csharp
/// <summary>
/// Convenience: disable compression for this response
/// </summary>
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public HttpResponse NoCompression()
```


## SendAsync

```csharp
/// <summary>
/// Send the response now
/// </summary>
/// <returns></returns>
public async ValueTask SendAsync()
```

This is the proper way of sending data a client in you want to do it on your own.
But any object return by a delegate will be default serialized and send as json.

See: 


## 

```csharp

```

## 

```csharp

```


## DefaultContentType

```csharp
/// <summary>
/// Get Content Type from a file extension (e.g: ".html")
/// </summary>
/// <param name="extension"></param>
/// <returns></returns>
public static string DefaultContentType(string extension)
```

## DefaultStatusText

```csharp
/// <summary>
/// Get the Default Status Text for a status code
/// </summary>
/// <param name="code"></param>
/// <returns></returns>
public static string DefaultStatusText(int code)
```


## Aliases

The following methods provide a more convenient way for most of the [common response](../guide/api-response.md#helpers).
