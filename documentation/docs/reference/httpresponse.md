# HttpResponse

This class can be used to build a response which will be sent to the client.

As already said in the [guide](../guide/api-response#response-property), a `Response` can be returned by a `Controller` method
and it will be sent async to the client.


## Constructor

```csharp
/// <summary>
/// Initialize an empty HTTP response
/// </summary>
public HttpResponse()
```

```csharp
/// <summary>
/// Initialize a new HTTP response with a given status and protocol
/// </summary>
/// <param name="status">HTTP status</param>
/// <param name="protocol">Protocol version (default is "HTTP/1.1")</param>
public HttpResponse(int status, string protocol = "HTTP/1.1")
```

```csharp
/// <summary>
/// Initialize a new HTTP response with a given status, status phrase and protocol
/// </summary>
/// <param name="status">HTTP status</param>
/// <param name="statusPhrase">HTTP status phrase</param>
/// <param name="protocol">Protocol version</param>
public HttpResponse(int status, string statusPhrase, string protocol)
```

::: tip NOTE
The way `HttpReponse` is currenlty build implies that methods must be call in a order by the data appear in the Http Response.
1. `new HttpResponse()` or `Clear()` for existing response.
2. [`SetHeader()`](#setheader) : optionnal
3. [`SetCookie()`](#setcookie) : optionnal
4. [`SetBegin()`](#setbegin) : required
5. [`SetCORSHeaders()`](#setcorsheaders) : optionnal
6. [`SetContentType()`](#setcontenttype) : optionnal
7. [`SetBody()`](#setbegin) : optionnal
:::


## Clear()

```csharp
/// <summary>
/// Clear the HTTP response cache
/// </summary>
public HttpResponse Clear()
```


## SetHeader()

```csharp
/// <summary>
/// Set the HTTP response header
/// </summary>
/// <param name="key">Header key</param>
/// <param name="value">Header value</param>
public HttpResponse SetHeader(string key, string value)
```

::: tip NOTE
Any call to `SetHeader()` whereas the `SetBody()` as already be called will be ignore.
This is constraint is inherited from NetCoreServer and will be remove in next release of SimpleW.
Known issues : [1](https://github.com/chronoxor/NetCoreServer/issues/224), [2](https://github.com/chronoxor/NetCoreServer/issues/145).
:::


## SetCookie()

```csharp
/// <summary>
/// Set the HTTP response cookie
/// </summary>
/// <param name="name">Cookie name</param>
/// <param name="value">Cookie value</param>
/// <param name="maxAge">Cookie age in seconds until it expires (default is 86400)</param>
/// <param name="path">Cookie path (default is "")</param>
/// <param name="domain">Cookie domain (default is "")</param>
/// <param name="secure">Cookie secure flag (default is true)</param>
/// <param name="strict">Cookie strict flag (default is true)</param>
/// <param name="httpOnly">Cookie HTTP-only flag (default is true)</param>
public HttpResponse SetCookie(string name, string value, int maxAge = 86400, string path = "", string domain = "", bool secure = true, bool strict = true, bool httpOnly = true)
```


## SetBegin()

```csharp
/// <summary>
/// Set the HTTP response begin with a given status, status phrase and protocol
/// </summary>
/// <param name="status">HTTP status</param>
/// <param name="statusPhrase"> HTTP status phrase</param>
/// <param name="protocol">Protocol version</param>
public HttpResponse SetBegin(int status, string statusPhrase, string protocol)
```


## SetCORSHeaders()

```csharp
/// <summary>
/// Set Header when CORS is enabled
/// </summary>
public void SetCORSHeaders()
```

```csharp
/// <summary>
/// Set Header to response parameter when CORS is enabled
/// </summary>
/// <param name="response"></param>
public static void SetCORSHeaders(HttpResponse response)
```


## SetContentType()

```csharp
/// <summary>
/// Set the HTTP response content type
/// </summary>
/// <param name="extension">Content extension</param>
public HttpResponse SetContentType(string extension)
```


## SetBody()

```csharp
/// <summary>
/// Set the HTTP response body
/// </summary>
/// <param name="body">Body string content (default is "")</param>
public HttpResponse SetBody(string body = "")
```

```csharp
/// <summary>
/// Set the HTTP response body
/// </summary>
/// <param name="body">Body string content as a span of characters</param>
public HttpResponse SetBody(ReadOnlySpan<char> body)
```

```csharp
/// <summary>
/// Set the HTTP response body
/// </summary>
/// <param name="body">Body binary content</param>
public HttpResponse SetBody(byte[] body)
```

```csharp
/// <summary>
/// Set the HTTP response body
/// </summary>
/// <param name="body">Body binary content as a span of bytes</param>
public HttpResponse SetBody(ReadOnlySpan<byte> body)
```

## Helpers

The following methods provide many options to customize [common response](../guide/api-response.md#helpers).

### MakeResponse()

```csharp
/// <summary>
/// Make Response from string
/// </summary>
/// <param name="content">The string Content.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">The string array of supported compress types (default null)</param>
public HttpResponse MakeResponse(string content, string contentType = "application/json; charset=UTF-8", string[] compress = null)
```

```csharp
/// <summary>
/// Make Response from byte[]
/// </summary>
/// <param name="content">byte[] Content.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">The string array of supported compress types (default null)</param>
public HttpResponse MakeResponse(byte[] content, string contentType = "application/json; charset=UTF-8", string[] compress = null)
```

The `MakeResponse()` will create a text Response to the client.


### MakeDownloadResponse()

```csharp
/// <summary>
/// Make Download response
/// </summary>
/// <param name="content">The MemoryStream Content.</param>
/// <param name="output_filename">name of the download file.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">The string array of supported compress types (default null)</param>
public HttpResponse MakeDownloadResponse(MemoryStream content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", string[] compress = null)
```

```csharp
/// <summary>
/// Make Download response
/// </summary>
/// <param name="content">The string Content.</param>
/// <param name="output_filename">name of the download file.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">The string array of supported compress types (default null)</param>
public HttpResponse MakeDownloadResponse(string content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", string[] compress = null)
```

```csharp
/// <summary>
/// Make Download response
/// </summary>
/// <param name="content">The byte[] Content.</param>
/// <param name="output_filename">name of the download file.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">The string array of supported compress types (default null)</param>
public HttpResponse MakeDownloadResponse(byte[] content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", string[] compress = null)
```

The `MakeDownloadResponse()` will create a binary Response forcing client to download file.


### MakeUnAuthorizedResponse()

```csharp
/// <summary>
/// Make UnAuthorized response
/// </summary>
/// <param name="content">Error content (default is "Server UnAuthorized Access")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeUnAuthorizedResponse(string content = "Server UnAuthorized Access", string contentType = "text/plain; charset=UTF-8")
```

The `MakeUnAuthorizedResponse()` will create 401 response error code.


### MakeForbiddenResponse()

```csharp
/// <summary>
/// Make Forbidden response
/// </summary>
/// <param name="content">Error content (default is "Server Forbidden Access")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeForbiddenResponse(string content = "Server Forbidden Access", string contentType = "text/plain; charset=UTF-8")
```

The `MakeForbiddenResponse()` will create 403 response error code.


### MakeInternalServerErrorResponse()

```csharp
/// <summary>
/// Make ServerInternalError response
/// </summary>
/// <param name="content">Error content (default is "Server Internal Error")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeInternalServerErrorResponse(string content = "Server Internal Error", string contentType = "text/plain; charset=UTF-8")
```

The `MakeInternalServerErrorResponse()` will create 500 response error code.


### MakeNotFoundResponse()

```csharp
/// <summary>
/// Make NotFound response
/// </summary>
/// <param name="content">Error content (default is "Not Found")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeNotFoundResponse(string content = "Not Found", string contentType = "text/plain; charset=UTF-8")
```

The `MakeNotFoundResponse()` will create 404 response error code.


### MakeRedirectResponse()

```csharp
/// <summary>
/// Make Redirect Tempory Response (status code 302)
/// </summary>
/// <param name="location">The string location.</param>
public HttpResponse MakeRedirectResponse(string location)
```

The `MakeRedirectResponse()` will create 302 response code to redirect client to `location`.


### MakeServerSentEventsResponse()

```csharp
/// <summary>
/// Response for initializing Server Sent Events
/// </summary>
/// <returns></returns>
public HttpResponse MakeServerSentEventsResponse()
```

The `MakeServerSentEventsResponse()` will create a Server Sent Event response and so, let the connection open for the client.

