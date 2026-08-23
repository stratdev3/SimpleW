# HttpSession

`HttpSession` represents one client connection. It owns the transport used to receive HTTP requests and send responses, and it is reused for every HTTP/1.x request received on that connection, including pipelined requests.

The current request and response are exposed through [`Request`](#request) and [`Response`](#response). Request-scoped state (`Response`, `Principal`, `Metadata`, and `Bag`) is reset before each request is dispatched.

::: warning
Do not keep references to `Request`, `Response`, `Bag`, or their contents after the handler has completed. The session reuses these objects and may return request-body buffers to the shared pool.
:::


## Constructor

```csharp
public HttpSession(SimpleWServer server, ISimpleWEngine transport, ArrayPool<byte> bufferPool)
```

The constructor creates the reusable request parser, [`HttpRequest`](./httprequest.md), and [`HttpResponse`](./httpresponse.md), assigns a new session [`Id`](#id), and initializes idle-timeout tracking.

`HttpSession` is normally created and managed by [`SimpleWServer.CreateSessionAsync(...)`](./simplewserver.md). A custom engine should pass each accepted connection to `CreateSessionAsync(...)` instead of constructing, connecting, processing, and disposing the session itself.


## Id

```csharp
/// <summary>
/// Gets the unique identifier of this session.
/// </summary>
public Guid Id { get; }
```

The identifier is generated once by the constructor and remains the same for the lifetime of the connection. It can be passed to [`SimpleWServer.FindSession(...)`](./simplewserver.md) while the session is registered.


## Server

```csharp
/// <summary>
/// Owning SimpleWServer instance.
/// </summary>
public readonly SimpleWServer Server;
```

This field gives handlers and [`Controller`](./controller.md) classes access to the server that owns the session.


## Request

```csharp
/// <summary>
/// Gets the current request.
/// </summary>
public HttpRequest Request { get; }
```

The parser populates this reusable [`HttpRequest`](./httprequest.md) before dispatching the matched handler. On a keep-alive connection, the same instance is populated again for the next request. SimpleW supports HTTP pipelining and may already have received subsequent requests in the transport buffer.


## Response

```csharp
/// <summary>
/// Gets the current response.
/// </summary>
public HttpResponse Response { get; }
```

The reusable [`HttpResponse`](./httpresponse.md) is reset before every handler dispatch. Use it to build or send the response.

::: tip
Prefer `Response` over the low-level [`SendAsync`](#sendasync) overloads. `HttpResponse` handles headers, body ownership, byte accounting, response notification, compression, and other response behavior.
:::


## Principal

```csharp
/// <summary>
/// Current user principal.
/// If not already resolved, calls Server.PrincipalResolver when defined.
/// </summary>
public HttpPrincipal Principal { get; set; }
```

The principal is resolved lazily the first time this property is read during a request. `Server.PrincipalResolver` is called at most once for that request; when no resolver is configured, the value is `HttpPrincipal.Anonymous`.

Assigning the property replaces the value and marks it as resolved. The principal is reset to anonymous before the next request on the same connection. See the [principal guide](../guide/principal.md).


## Metadata

```csharp
/// <summary>
/// Metadata attached to the currently matched handler.
/// Empty when no metadata is defined or no route matched yet.
/// </summary>
public HandlerMetadataCollection Metadata { get; internal set; }
```

The router assigns the metadata collected from the matched handler. It is reset to `HandlerMetadataCollection.Empty` before the next request. See the [handler attribute guide](../guide/handler-attribute.md).


## Bag

```csharp
/// <summary>
/// Per-request transient storage shared across middlewares/handlers.
/// </summary>
public HttpBag Bag { get; }
```

The [`HttpBag`](./httpbag.md) is allocated lazily and cleared before every request. Use it to share request-scoped values between middleware and handlers. See the [middleware example](../guide/middleware.md#bag-per-request-storage).


## JsonEngine

```csharp
/// <summary>
/// Gets the server JSON engine.
/// </summary>
public IJsonEngine JsonEngine => Server.JsonEngine;
```

This is a convenience property for `Server.JsonEngine`. See [`IJsonEngine`](./ijsonengine.md) and the [JSON engine replacement guide](../guide/replacing-core-components.md#replace-json-engine).


## Request cancellation

### RequestAborted

```csharp
/// <summary>
/// Gets a cancellation token that is triggered when the client disconnects.
/// </summary>
public CancellationToken RequestAborted { get; }
```

Pass this token to asynchronous I/O that should stop when the connection is lost:

```csharp
server.MapGet("/work", async (HttpSession session) => {
    await RunLongOperationAsync(session.RequestAborted);
    return session.Response.Text("Done");
});
```

Client-disconnection cancellation is cooperative. During an asynchronous handler, accessing `RequestAborted` enables the disconnect watcher when `SimpleWServerOptions.SocketDisconnectPollInterval` is not `TimeSpan.Zero`. The token is also cancelled when the session is aborted or disposed.

See the [handler cancellation guide](../guide/handler.md#cancellationtoken-requestaborted) for configuration, synchronous and asynchronous examples, and best practices.


### ThrowIfAborted

```csharp
/// <summary>
/// Throws if the request has been aborted.
/// </summary>
public void ThrowIfAborted()
```

This is equivalent to `RequestAborted.ThrowIfCancellationRequested()`. It is useful at safe interruption points in synchronous or CPU-bound work and throws `OperationCanceledException` after cancellation.


### AbortConnectionAsync

```csharp
/// <summary>
/// Aborts the underlying connection.
/// </summary>
public ValueTask AbortConnectionAsync(
    bool reset = false,
    CancellationToken cancellationToken = default)
```

This method first cancels `RequestAborted`, then delegates the connection abort to the underlying engine. Set `reset` to request a reset-style abort when the engine supports it. The supplied `cancellationToken` controls the abort operation; it is not the session's `RequestAborted` token.


## Connection information

### LocalEndPoint

```csharp
/// <summary>
/// Local endpoint when the transport exposes one.
/// </summary>
public EndPoint? LocalEndPoint { get; }
```

This is the local endpoint reported by the engine, or `null` when it is unavailable.


### RemoteEndPoint

```csharp
/// <summary>
/// Remote endpoint when the transport exposes one.
/// </summary>
public EndPoint? RemoteEndPoint { get; }
```

This is the peer endpoint reported by the engine, or `null` when it is unavailable.


### ClientIpAddress

```csharp
/// <summary>
/// Client IP address.
/// </summary>
public IPAddress? ClientIpAddress { get; }
```

Unlike `RemoteEndPoint`, this property is resolved through `Server.ClientIpResolver`. It can therefore account for a trusted proxy or another custom resolution policy. See the [custom client IP resolution guide](../guide/observability.md#custom-client-ip-resolution).


## TLS information

### IsEncrypted

```csharp
/// <summary>
/// Gets a value indicating whether this session uses an encrypted transport.
/// </summary>
public bool IsEncrypted { get; }
```

This value comes directly from the underlying engine.


### TLS metadata

```csharp
/// <summary>
/// Gets the application protocol selected during the TLS handshake.
/// </summary>
public string? NegotiatedApplicationProtocol { get; }

/// <summary>
/// Gets the subject of the authenticated client certificate in the transport's native format.
/// </summary>
public string? ClientCertificateSubject { get; }

/// <summary>
/// Gets the email address of the authenticated client certificate when available.
/// </summary>
public string? ClientCertificateEmailAddress { get; }

/// <summary>
/// Gets whether the client certificate was authenticated, or null when the transport does not expose TLS metadata.
/// </summary>
public bool? IsClientCertificateAuthenticated { get; }
```

These properties are populated only when the engine exposes `ISimpleWTransportTlsFeature`:

- `NegotiatedApplicationProtocol` is the protocol selected during the TLS handshake.
- `ClientCertificateSubject` is the authenticated client certificate subject in the engine's native format.
- `ClientCertificateEmailAddress` is the certificate email address when the engine can expose one. It may be `null` even when the certificate was authenticated.
- `IsClientCertificateAuthenticated` is `true` when a client certificate was presented and accepted, `false` when TLS metadata is available but no client certificate was authenticated, and `null` when the engine exposes no TLS metadata.

The engine validates the client certificate subject, but its textual representation is engine-specific. Do not parse it naively or assume that .NET and OpenSSL produce the same format.


## Connection state

### CloseAfterResponse

```csharp
/// <summary>
/// Marks whether the transport should be closed after the current response.
/// </summary>
public bool CloseAfterResponse { get; private set; }
```

SimpleW derives this value from the HTTP version and the request's `Connection` header. It is also set for fatal processing errors and request timeouts. [`HttpResponse`](./httpresponse.md) uses it when generating the response `Connection` header, and the processing loop uses it to decide whether another request can be read.


### LastActivityTick

```csharp
/// <summary>
/// Gets the last activity tick.
/// </summary>
public long LastActivityTick { get; private set; }
```

This is an `Environment.TickCount64` value used by the server's session idle-timeout logic. It is not a `DateTime` or a Unix timestamp.


### MarkActivity

```csharp
/// <summary>
/// Updates the last activity tick.
/// </summary>
public void MarkActivity()
```

Sets `LastActivityTick` to `Environment.TickCount64`. Normal HTTP processing updates it through `Server.MarkSession(this)` whenever transport data is received. Application handlers normally do not need to call it.


## SendAsync

```csharp
/// <summary>
/// SendAsync to transport.
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
public ValueTask SendAsync(ReadOnlyMemory<byte> buffer)

/// <summary>
/// SendAsync to transport.
/// </summary>
/// <param name="segments"></param>
/// <returns></returns>
public ValueTask SendAsync(ArraySegment<byte>[] segments)

/// <summary>
/// SendAsync to transport.
/// </summary>
/// <param name="header"></param>
/// <param name="body"></param>
/// <returns></returns>
public ValueTask SendAsync(ArraySegment<byte> header, ArraySegment<byte> body)

/// <summary>
/// SendAsync to transport.
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
public ValueTask SendAsync(ArraySegment<byte> buffer)
```

These are the lowest-level public write methods. Each overload writes directly to the current engine output and adds the number of bytes reported by the engine to `Response.BytesSent`. The engine output owns the single-writer guard.

If the write throws `IOException` or `ObjectDisposedException`, the session is aborted and the exception is not rethrown by these methods.

::: warning
These overloads do not construct an HTTP response and do not call [`NotifyResponseSent`](#notifyresponsesent). Use [`Response`](#response) for normal handler output.
:::


## Server and engine lifecycle

The members in this section are public because they participate in the server/engine lifecycle. Normal middleware, controllers, and handlers should not call them directly.


### Connect

```csharp
/// <summary>
/// Connect.
/// </summary>
public void Connect()
```

Marks the session as receiving. The operation is idempotent. [`SimpleWServer.CreateSessionAsync(...)`](./simplewserver.md) calls it before [`ProcessAsync`](#processasync).


### ProcessAsync

```csharp
/// <summary>
/// Runs the HTTP processing loop.
/// </summary>
public Task ProcessAsync()
```

The processing loop reads engine buffers, parses one or more HTTP requests, dispatches matched handlers through the router, advances consumed input, applies request-header and request-body slow-client protection, manages keep-alive, and generates error responses when processing fails.

It exits when the connection closes, is aborted, must close after a response, or when a module takes ownership of the transport. Custom engines should use `SimpleWServer.CreateSessionAsync(...)`, which manages this call and cleanup.


### NotifyResponseSent

```csharp
/// <summary>
/// Notifies the session that the response has been sent.
/// </summary>
public void NotifyResponseSent()
```

Completes session-level response logging and telemetry updates. [`HttpResponse.SendAsync()`](./httpresponse.md#sendasync) calls it automatically after a successful send. Calling it manually in addition to `HttpResponse.SendAsync()` would duplicate notifications.


### Dispose

```csharp
/// <summary>
/// Disposes the session.
/// </summary>
public void Dispose()
```

Disposal is idempotent. It cancels `RequestAborted`, disposes the underlying engine, and disposes the session cancellation source. `SimpleWServer` disposes registered sessions when they close; application code normally does not own this lifetime.


## Internal transport ownership

```csharp
/// <summary>
/// Underlying transport.
/// </summary>
internal ISimpleWEngine Transport { get; }

/// <summary>
/// Gets whether an external component owns the transport.
/// </summary>
internal bool IsTransportOwned { get; }

/// <summary>
/// Stops HTTP parsing and transfers transport ownership.
/// </summary>
internal bool TryTakeTransportOwnership()
```

These internal members are used by connection-upgrade components such as the WebSocket and Server-Sent Events modules. `TryTakeTransportOwnership()` succeeds only for the first caller. After ownership is transferred, `HttpSession` stops reading, advancing, flushing, and idle-timeout management for that transport; the new owner becomes responsible for it.


## FakeHttpRequest

```csharp
/// <summary>
/// Creates a fake request for testing purposes.
/// </summary>
public static bool FakeHttpRequest(
    ref ReadOnlySequence<byte> buffer,
    HttpRequest request)
```

This test helper resets `request` and returns `false` when `buffer` is empty. Otherwise it consumes the entire buffer, creates a fixed `GET /api/test/hello HTTP/1.1` request with empty headers, and returns `true`. It does not parse the supplied bytes into an HTTP request.


## HttpRequestException

```csharp
public sealed class HttpRequestException : Exception
```

`HttpSession.ProcessAsync()` catches this exception and sends an error response using its HTTP and diagnostic properties, then closes the connection.


### Constructor

```csharp
public HttpRequestException(
    string message,
    int statusCode,
    string? statusText = null,
    string? displayName = "HTTP Parse")
```

- `message` is the exception message used for diagnostics.
- `statusCode` is the HTTP response status code.
- `statusText` defaults to `HttpResponse.DefaultStatusText(statusCode)` when it is `null`.
- `displayName` labels diagnostics and telemetry. Omitting it uses `"HTTP Parse"`; passing `null` uses the resolved status text.


### Properties

```csharp
public int StatusCode { get; }
public string StatusText { get; }
public string DisplayName { get; }
```
