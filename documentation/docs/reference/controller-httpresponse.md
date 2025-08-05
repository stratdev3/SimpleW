# HttpReponse

As already said in the [guide](../guide/api-response#helpers), the `Controller` 
is dealing with `HttpResponse` object to send async response to the client.

The `Response` property of type `HttpResponse` is a prefill property you can use to sent data to client.

You can use builders to sent the most common response to a client.


## MakeResponse()

```csharp
/// <summary>
/// Make Response from string
/// </summary>
/// <param name="content">The string Content.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeResponse(string content, string contentType = "application/json; charset=UTF-8")
```

```csharp
/// <summary>
/// Make Response from byte[]
/// </summary>
/// <param name="content">byte[] Content.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeResponse(byte[] content, string contentType = "application/json; charset=UTF-8")
```

```csharp
/// <summary>
/// Make Response from object with JsonSerializerSettings.Context.streamingContextObject StreamingContextStates.Other
/// </summary>
/// <param name="content">The object Content.</param>
/// <param name="settings">The JsonSerializerSettings settings (default is null)</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeResponse(object content, JsonSerializerSettings settings = null, string contentType = "application/json; charset=UTF-8")
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
/// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
HttpResponse MakeDownloadResponse(MemoryStream content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true)
```

```csharp
/// <summary>
/// Make Download response
/// </summary>
/// <param name="content">The string Content.</param>
/// <param name="output_filename">name of the download file.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
HttpResponse MakeDownloadResponse(string content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true)
```

```csharp
/// <summary>
/// Make Download response
/// </summary>
/// <param name="content">The byte[] Content.</param>
/// <param name="output_filename">name of the download file.</param>
/// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
/// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
HttpResponse MakeDownloadResponse(byte[] content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true)
```

The `MakeDownloadResponse()` will create a binary Response forcing client to download file.


## MakeAccessResponse()

```csharp
/// <summary>
/// Make Error Access response
/// </summary>
HttpResponse MakeAccessResponse()
```

The `MakeAccessResponse()` will create 401 or 403 response error code depending the status or [`webuser`](./controller-overview#webuser) property.


## MakeUnAuthorizedResponse()

```csharp
/// <summary>
/// Make UnAuthorized response
/// </summary>
/// <param name="content">Error content (default is "Server UnAuthorized Access")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeUnAuthorizedResponse(string content = "Server UnAuthorized Access", string contentType = "text/plain; charset=UTF-8")
```

The `MakeUnAuthorizedResponse()` will create 401 response error code.


## MakeForbiddenResponse()

```csharp
/// <summary>
/// Make Forbidden response
/// </summary>
/// <param name="content">Error content (default is "Server Forbidden Access")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeForbiddenResponse(string content = "Server Forbidden Access", string contentType = "text/plain; charset=UTF-8")
```

The `MakeForbiddenResponse()` will create 403 response error code.


## MakeInternalServerErrorResponse()

```csharp
/// <summary>
/// Make ServerInternalError response
/// </summary>
/// <param name="content">Error content (default is "Server Internal Error")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeInternalServerErrorResponse(string content = "Server Internal Error", string contentType = "text/plain; charset=UTF-8")
```

The `MakeInternalServerErrorResponse()` will create 500 response error code.


## MakeNotFoundResponse()

```csharp
/// <summary>
/// Make NotFound response
/// </summary>
/// <param name="content">Error content (default is "Not Found")</param>
/// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
HttpResponse MakeNotFoundResponse(string content = "Not Found", string contentType = "text/plain; charset=UTF-8")
```

The `MakeNotFoundResponse()` will create 404 response error code.


## MakeRedirectResponse()

```csharp
/// <summary>
/// Make Redirect Tempory Response (status code 302)
/// </summary>
/// <param name="location">The string location.</param>
HttpResponse MakeRedirectResponse(string location)
```

The `MakeRedirectResponse()` will create 302 response code to redirect client to `location`.


## MakeServerSentEventsResponse()

```csharp
/// <summary>
/// Response for initializing Server Sent Events
/// </summary>
/// <returns></returns>
HttpResponse MakeServerSentEventsResponse()
```

The `MakeServerSentEventsResponse()` will create a Server Sent Event response and so, let the connection open for the client.


## AddSSESession()

```csharp
/// <summary>
/// Flag the current Session as SSE Session
/// and add it to the server SSESessions
/// Alias for Session.AddSSESession();
/// </summary>
void AddSSESession()
```

This method flag the current `HttpSession` as a Server Sent Events session and add it the list of `SSESessions`.
By doing so, the server will be able to BroadCastSSEMessage()

