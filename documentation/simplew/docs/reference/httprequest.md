# HttpRequest

This class all informations of the client request.


## Method

```csharp
/// <summary>
/// Get the HTTP request method
/// </summary>
public string Method { get; private set; }
```

## Path

```csharp
/// <summary>
/// Get the HTTP request Path
/// </summary>
public string Path { get; private set; }
```

## RawTarget

```csharp
/// <summary>
/// Raw target (path + query)
/// </summary>
public string RawTarget { get; private set; }
```

## Protocol

```csharp
/// <summary>
/// HTTP protocol version (e.g. "HTTP/1.1")
/// </summary>
public string Protocol { get; private set; }
```

## Headers

```csharp
/// <summary>
/// HTTP headers (case-insensitive)
/// </summary>
public HttpHeaders Headers { get; private set; }
```

See [`HttpHeaders`](./httpheaders.md) class for more informations


## Body

```csharp
/// <summary>
/// Body (buffer only valid during the time of the underlying Handler)
/// </summary>
public ReadOnlySequence<byte> Body { get; private set; }
```

## BodyString

```csharp
/// <summary>
/// Body as String (only valid during the time of the underlying Handler)
/// </summary>
public string BodyString
```

## QueryString

```csharp
/// <summary>
/// QueryString as String (e.g: key1=value1&key2=value2)
/// </summary>
public string QueryString { get; private set; }
```

## Query

```csharp
/// <summary>
/// QueryString as Dictionnary
/// </summary>
public Dictionary<string, string> Query
```

## RouteValues

```csharp
/// <summary>
/// Route values extracted from matched route (ex: :id, :path*)
/// Null when route has no parameters.
/// </summary>
public Dictionary<string, string>? RouteValues { get; private set; }
```

## BodyMap()

```csharp
/// <summary>
/// Update the model with data from POST
/// </summary>
/// <param name="request">The HttpRequest request.</param>
/// <param name="model">The Model instance to populate.</param>
/// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
/// <param name="excludeProperties">string array of properties to not update.</param>
/// <param name="jsonEngine">the json library to handle serialization/deserialization</param>
/// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
public static bool BodyMap<TModel>(this HttpRequest request, TModel model, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null, IJsonEngine? jsonEngine = null)
```

## BodyMapAnonymous()

```csharp
/// <summary>
/// Update the anonymous model with data from POST
/// </summary>
/// <param name="request">The HttpRequest request.</param>
/// <param name="model">The Anonymous Model instance to populate.</param>
/// <param name="jsonEngine">the json library to handle serialization/deserialization</param>
/// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
public static bool BodyMapAnonymous<TModel>(this HttpRequest request, ref TModel model, IJsonEngine? jsonEngine = null)
```

## BodyMultipart()

```csharp
/// <summary>
/// Get MultipartFormData from an HttpRequest
/// </summary>
/// <param name="request"></param>
/// <param name="maxParts"></param>
/// <param name="maxFileBytes"></param>
/// <returns></returns>
public static MultipartFormData? BodyMultipart(this HttpRequest request, int maxParts = 200, int maxFileBytes = 50 * 1024 * 1024)
```

## BodyMultipart()

```csharp
/// <summary>
/// Get MultipartFormDataStream from an HttpRequest
/// </summary>
/// <param name="request"></param>
/// <param name="onField"></param>
/// <param name="onFile"></param>
/// <param name="maxParts"></param>
/// <param name="maxFileBytes"></param>
/// <returns></returns>
public static bool BodyMultipartStream(this HttpRequest request, Action<string, string>? onField = null, Action<MultipartFileInfo, ReadOnlySequence<byte>>? onFile = null, int maxParts = 200, long maxFileBytes = 50 * 1024 * 1024)
```

## BodyForm()

```csharp
/// <summary>
/// Parse application/x-www-form-urlencoded request body readonlysequence byte.
/// - supports repeated keys => List&lt;string?&gt;
/// - trims the trailing [] convention (key[]=a&amp;key[]=b)
/// - decodes + and %xx using UTF-8
/// </summary>
/// <param name="request"></param>
public static Dictionary<string, object?> BodyForm(this HttpRequest request)
```
