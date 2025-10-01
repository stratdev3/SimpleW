# HttpReponse

This class can be used to build a response which will be sent to the client.

As already said in the [guide](../guide/api-response#helpers), a `Response` can be returned by a `Controller` method
and it will be sent async to the client.


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


## MakeResponse()

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


## MakeDownloadResponse()

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


## MakeUnAuthorizedResponse()

```csharp
/// <summary>
/// Make UnAuthorized response
/// </summary>
/// <param name="content">Error content (default is "Server UnAuthorized Access")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeUnAuthorizedResponse(string content = "Server UnAuthorized Access", string contentType = "text/plain; charset=UTF-8")
```

The `MakeUnAuthorizedResponse()` will create 401 response error code.


## MakeForbiddenResponse()

```csharp
/// <summary>
/// Make Forbidden response
/// </summary>
/// <param name="content">Error content (default is "Server Forbidden Access")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeForbiddenResponse(string content = "Server Forbidden Access", string contentType = "text/plain; charset=UTF-8")
```

The `MakeForbiddenResponse()` will create 403 response error code.


## MakeInternalServerErrorResponse()

```csharp
/// <summary>
/// Make ServerInternalError response
/// </summary>
/// <param name="content">Error content (default is "Server Internal Error")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeInternalServerErrorResponse(string content = "Server Internal Error", string contentType = "text/plain; charset=UTF-8")
```

The `MakeInternalServerErrorResponse()` will create 500 response error code.


## MakeNotFoundResponse()

```csharp
/// <summary>
/// Make NotFound response
/// </summary>
/// <param name="content">Error content (default is "Not Found")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
public HttpResponse MakeNotFoundResponse(string content = "Not Found", string contentType = "text/plain; charset=UTF-8")
```

The `MakeNotFoundResponse()` will create 404 response error code.


## MakeRedirectResponse()

```csharp
/// <summary>
/// Make Redirect Tempory Response (status code 302)
/// </summary>
/// <param name="location">The string location.</param>
public HttpResponse MakeRedirectResponse(string location)
```

The `MakeRedirectResponse()` will create 302 response code to redirect client to `location`.


## MakeServerSentEventsResponse()

```csharp
/// <summary>
/// Response for initializing Server Sent Events
/// </summary>
/// <returns></returns>
public HttpResponse MakeServerSentEventsResponse()
```

The `MakeServerSentEventsResponse()` will create a Server Sent Event response and so, let the connection open for the client.

