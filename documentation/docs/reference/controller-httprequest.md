# HttpRequest

This `Request` property contains all informations of the client request in the `HttpRequest` class format.

## Method

```csharp
/// <summary>
/// Get the HTTP request method
/// </summary>
string Method { get; private set }
```


## Url

```csharp
/// <summary>
/// Get the HTTP request URL
/// </summary>
string Url { get; private set }
```


## Protocol

```csharp
/// <summary>
/// Get the HTTP request protocol version
/// </summary>
string Protocol { get; private set }
```

## Body

```csharp
/// <summary>
/// Get the HTTP request body as string
/// </summary>
string Body { get; private set }
```


## BodyBytes

```csharp
/// <summary>
/// Get the HTTP request body as byte array
/// </summary>
byte[] BodyBytes { get; private set }
```

## BodySpan

```csharp
/// <summary>
/// Get the HTTP request body as byte span
/// </summary>
Span<byte> BodySpan { get; private set }
```

## BodyLength

```csharp
/// <summary>
/// Get the HTTP request body length
/// </summary>
long BodyLength { get; private set }
```


## Cookies

```csharp
/// <summary>
/// Get the HTTP request cookies count
/// </summary>
long Cookies { get; private set }
```

Used to get the cookies count and iterate through [`Cookie()`](#cookie)


## Cookie()

```csharp
/// <summary>
/// Get the HTTP request cookie by index
/// </summary>
(string, string) Cookie(int i)
```


## Header()

```csharp
/// <summary>
/// Return Header Value for a specified header name for a Request
/// </summary>
/// <param name="name">The Header Name.</param>
/// <returns><c>string value</c> of the header if exists in the Request; otherwise, <c>null</c>.</returns>
string Header(string name)
```

This method return the string content of the header by its name.


## BodyMap()

```csharp
/// <summary>
/// Update the model with data from POST
/// </summary>
/// <param name="model">The Model instance to populate.</param>
/// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
/// <param name="excludeProperties">string array of properties to not update.</param>
/// <param name="settings">JsonSerializerSettings for the JsonConvert.PopulateObject() method.</param>
/// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
bool BodyMap<TModel>(TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, JsonSerializerSettings settings = null)
```


## BodyMapAnonymous()

```csharp
/// <summary>
/// Update the anonymous model with data from POST
/// </summary>
/// <param name="model">The Anonymous Model instance to populate.</param>
/// <param name="settings">JsonSerializerSettings for the JsonConvert.PopulateObject() method.</param>
/// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
bool BodyMapAnonymous<TModel>(ref TModel model, JsonSerializerSettings settings = null)
```


## BodyFile()

```csharp
/// <summary>
/// Parses multipart/form-data and contentType given the request body (Request.InputStream)
/// Please note the underlying input stream is not rewindable.
/// </summary>
/// <returns>MultipartFormDataParser</returns>
MultipartFormDataParser BodyFile()
```


## BodyForm()

```csharp
/// <summary>
/// Parses application/x-www-form-urlencoded contentType given the request body string.
/// </summary>
/// <param name="requestBody">The string request body.</param>
/// <returns>key/value data</returns>
Dictionary<string, object> BodyForm(string requestBody)
```
