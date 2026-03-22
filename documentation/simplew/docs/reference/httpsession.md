# HttpSession

The `HttpSession` is class responsible of receiving and sending data to a client :
- The received data are structured as a `HttpRequest` in the [`Request`](#request) property.
- The sending data are structured as a `HttpResponse` in the [`Response`](#response) property.


## Server

```csharp
/// <summary>
/// Underlying SimpleW Server
/// </summary>
public readonly SimpleWServer Server;
```

This property can be used to control [`Server`](./simplewserver.md) from any [`Controller`](./controller.md) class.


## JsonEngine

```csharp
/// <summary>
/// Expose the Server.JsonEngine
/// </summary>
public IJsonEngine JsonEngine;
```


## Request

```csharp
/// <summary>
/// Last HttpRequest Parsed
/// </summary>
public HttpRequest Request;
```

When `HttpSession` receive data from a client, it parses its content into a [`HttpRequest`](./httprequest.md) object and set the `Request` property.
It supports http pipelining.


## Response

```csharp
/// <summary>
/// Current HttpResponse
/// </summary>
public HttpResponse Response;
```

Before `HttpSession` call the Dispatcher to execute the underlying handler of the matched Route, it instanciates a new [`HttpResponse`](./httpresponse.md) object and set the `Response` property.

::: tip NOTE
You should always used the `Response` to send data to a client.
:::


## Principal

```csharp
/// <summary>
/// Principal
/// </summary>
public HttpPrincipal Principal
```

The current [`HttpPrincipal`](../reference/httpprincipal.md) use for this session. See 


## Bag

```csharp
/// <summary>
/// Per-request transient storage shared across middlewares/handlers.
/// </summary>
public HttpBag Bag;
```

## ClientCertificate

```csharp
/// <summary>
/// ClientCertificate if exists in sslStream
/// </summary>
public X509Certificate2? ClientCertificate
```


## SendAsync

::: warning
The `SendAsync` methods bellow are the lowest level to send data to client. They are barely aliases of `Socket.SendAsync()` with a thread-safe guard.
You should never need to use them but instead use the `Response` property to send data to a client.
:::

```csharp
/// <summary>
/// SendAsync native to socket (thread safe)
/// Lower level of sending
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
/// <exception cref="InvalidOperationException"></exception>
public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer)
```

```csharp
/// <summary>
/// SendAsync to socket (thread safe)
/// Lower level of sending
/// </summary>
/// <param name="segments"></param>
/// <returns></returns>
public async ValueTask SendAsync(ArraySegment<byte>[] segments)
```

```csharp
/// <summary>
/// SendAsync to socket (thread safe)
/// Lower level of sending
/// </summary>
/// <param name="header"></param>
/// <param name="body"></param>
/// <returns></returns>
public async ValueTask SendAsync(ArraySegment<byte> header, ArraySegment<byte> body)
```

```csharp
/// <summary>
/// SendAsync to socket (thread safe)
/// Lower level of sending
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
/// <exception cref="InvalidOperationException"></exception>
public async ValueTask SendAsync(ArraySegment<byte> buffer)
```
