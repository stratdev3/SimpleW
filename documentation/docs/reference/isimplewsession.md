# ISimpleWSession

The interface is both implemented by  `SimpleWSession` or `SimpleWSSession`.


## Server

```csharp
/// <summary>
/// Server Instance
/// </summary>
public new ISimpleWServer Server;
```

This property can be used to control [`Server`](./simplewserver) from any `Controller` class.


## jwt

```csharp
/// <summary>
/// JWT
/// </summary>
public string jwt { get; set; }
```


## webuser

```csharp
/// <summary>
/// <para>Get Current IWebUser</para>
/// <para>set by the underlying Controller.webuser
///       The only use case to have a webuser
///       property here is for logging</para>
/// </summary>
public IWebUser webuser { get; set; }
```


## SendResponseBody

This is the lower level, and it consists of sending bytes data to the client.

```csharp
/// <summary>
/// Send the HTTP response body (synchronous)
/// </summary>
/// <param name="body">HTTP response body</param>
/// <returns>Size of sent data</returns>
public long SendResponseBody(string body);
```

```csharp
/// <summary>
/// Send the HTTP response body (synchronous)
/// </summary>
/// <param name="body">HTTP response body as a span of characters</param>
/// <returns>Size of sent data</returns>
public long SendResponseBody(ReadOnlySpan<char> body);
```

```csharp
/// <summary>
/// Send the HTTP response body (synchronous)
/// </summary>
/// <param name="buffer">HTTP response body buffer</param>
/// <returns>Size of sent data</returns>
public long SendResponseBody(byte[] buffer);
```

```csharp
/// <summary>
/// Send the HTTP response body (synchronous)
/// </summary>
/// <param name="buffer">HTTP response body buffer</param>
/// <param name="offset">HTTP response body buffer offset</param>
/// <param name="size">HTTP response body size</param>
/// <returns>Size of sent data</returns>
public long SendResponseBody(byte[] buffer, long offset, long size);
```

```csharp
/// <summary>
/// Send the HTTP response body (synchronous)
/// </summary>
/// <param name="buffer">HTTP response body buffer as a span of bytes</param>
/// <returns>Size of sent data</returns>
public long SendResponseBody(ReadOnlySpan<byte> buffer)
```
