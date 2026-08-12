# HttpSession

The `HttpSession` is class responsible of receiving and sending data to a client :
- The received data are structured as a `HttpRequest` in the [`Request`](#request) property.
- The sending data are structured as a `HttpResponse` in the [`Response`](#response) property.


## Constructor

```csharp
public HttpSession(SimpleWServer server, ISimpleWEngine transport, ArrayPool<byte> bufferPool)
```

`HttpSession` is usually created by [`SimpleWServer.CreateSessionAsync(...)`](./simplewserver.md).
Custom engines should pass accepted connections to `CreateSessionAsync(...)` instead of creating sessions directly.


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

See the [interface](./isimplewengine.md). See the [examples](../guide/jsonengine.md).


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

The current [`HttpPrincipal`](../reference/httpprincipal.md) use for this session. See the [examples](../guide/principal.md).


## Metadata

```csharp
/// <summary>
/// Metadata attached to the currently matched handler.
/// Empty when no metadata is defined or no route matched yet.
/// </summary>
public HandlerMetadataCollection Metadata { get; internal set; }
```

See the [examples](../guide/handler-attribute.md).


## Bag

```csharp
/// <summary>
/// Per-request transient storage shared across middlewares/handlers.
/// </summary>
public HttpBag Bag;
```

See the [class](./httpbag.md). See the [examples](../guide/middleware.md#bag-per-request-storage).


## ClientIPAddress

```csharp
/// <summary>
/// Client IPAddress
/// </summary>
/// <returns></returns>
public IPAddress? ClientIpAddress
```

See the [examples](../guide/observability.md#custom-client-ip-resolution).


## TLS metadata

```csharp
public string? NegotiatedApplicationProtocol { get; }
public string? ClientCertificateSubject { get; }
public string? ClientCertificateEmailAddress { get; }
public bool? IsClientCertificateAuthenticated { get; }
```

`IsClientCertificateAuthenticated` is `true` when a client certificate was presented and accepted by the TLS policy, `false` when TLS metadata is available but no client certificate was authenticated, and `null` when the engine exposes no TLS metadata.

The client certificate subject is validated by the transport but retains the engine's native format. Do not parse it naively or assume that .NET and OpenSSL produce the same textual representation.

`ClientCertificateEmailAddress` contains the certificate email address when the TLS engine exposes it. It can be `null` even when `IsClientCertificateAuthenticated` is `true`.


## SendAsync

::: warning
The `SendAsync` methods bellow are the lowest level to send data to client. They write to the current `ISimpleWEngine` output, then update `Response.BytesSent` from the byte count returned by the transport output. The transport output owns the single-writer guard.
You should never need to use them but instead use the `Response` property to send data to a client.
:::

```csharp
/// <summary>
/// SendAsync native to transport
/// Lower level of sending
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
public async ValueTask SendAsync(ReadOnlyMemory<byte> buffer)
```

```csharp
/// <summary>
/// SendAsync to transport
/// Lower level of sending
/// </summary>
/// <param name="segments"></param>
/// <returns></returns>
public async ValueTask SendAsync(ArraySegment<byte>[] segments)
```

```csharp
/// <summary>
/// SendAsync to transport
/// Lower level of sending
/// </summary>
/// <param name="header"></param>
/// <param name="body"></param>
/// <returns></returns>
public async ValueTask SendAsync(ArraySegment<byte> header, ArraySegment<byte> body)
```

```csharp
/// <summary>
/// SendAsync to transport
/// Lower level of sending
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
public async ValueTask SendAsync(ArraySegment<byte> buffer)
```


## LocalEndPoint

```csharp
/// <summary>
/// Local endpoint when the transport exposes one.
/// </summary>
public EndPoint? LocalEndPoint { get; }
```


## RemoteEndPoint

```csharp
/// <summary>
/// Remote endpoint when the transport exposes one.
/// </summary>
public EndPoint? RemoteEndPoint { get; }
```


## IsSsl

```csharp
/// <summary>
/// Gets a value indicating whether this session uses an encrypted transport.
/// </summary>
public bool IsSsl { get; }
```


## AbortConnectionAsync

```csharp
/// <summary>
/// Aborts the underlying connection.
/// </summary>
public ValueTask AbortConnectionAsync(bool reset = false, CancellationToken cancellationToken = default)
```


## TryTakeTransportOwnership

```csharp
/// <summary>
/// Stops HTTP parsing and transfers transport ownership.
/// </summary>
internal bool TryTakeTransportOwnership()
```
