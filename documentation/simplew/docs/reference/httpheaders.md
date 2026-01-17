# HttpHeaders

This class is used to store the http headers included cookies. 
The most commons header have their own public property to get easier use, the others are stored in a array.

```csharp
/// <summary>
/// Host
/// </summary>
public string? Host;
```

```csharp
/// <summary>
/// Content-Type
/// </summary>
public string? ContentType;
```

```csharp
/// <summary>
/// Content-Length
/// raw string "123", parser use long
/// </summary>
public string? ContentLengthRaw;
```

```csharp
/// <summary>
/// User Agent
/// </summary>
public string? UserAgent;
```

```csharp
/// <summary>
/// Accept
/// </summary>
public string? Accept;
```

```csharp
/// <summary>
/// Accept Encoding
/// </summary>
public string? AcceptEncoding;
```

```csharp
/// <summary>
/// Accept Language
/// </summary>
public string? AcceptLanguage;
```

```csharp
/// <summary>
/// Contection
/// </summary>
public string? Connection;
```

```csharp
/// <summary>
/// Transfert Encoding
/// </summary>
public string? TransferEncoding;
```

```csharp
/// <summary>
/// Cookie
/// </summary>
public string? Cookie;
```

```csharp
/// <summary>
/// Upgrade
/// </summary>
public string? Upgrade;
```

```csharp
/// <summary>
/// Authorization
/// </summary>
public string? Authorization;
```

```csharp
/// <summary>
/// SecWebSocketKey
/// </summary>
public string? SecWebSocketKey;
```

```csharp
/// <summary>
/// SecWebSocketVersion
/// </summary>
public string? SecWebSocketVersion;
```

```csharp
/// <summary>
/// SecWebSocketProtocol
/// </summary>
public string? SecWebSocketProtocol;
```

```csharp
/// <summary>
/// Add a Header (name/value).
/// Set most common else save in _other
/// </summary>
public void Add(string name, string value);
```

```csharp
/// <summary>
/// TryGetValue (headers connus + autres).
/// </summary>
/// <param name="name"></param>
/// <param name="value"></param>
/// <returns></returns>
public bool TryGetValue(string name, out string? value)
```

```csharp
/// <summary>
/// Enumerated all headers (most commons + others).
/// </summary>
public IEnumerable<KeyValuePair<string, string>> EnumerateAll()
```

```csharp
/// <summary>
/// Try get a cookie by name from the Cookie header.
/// Cookie names are compared case-sensitively (RFC).
/// </summary>
/// <param name="name"></param>
/// <param name="value"></param>
/// <returns></returns>
public bool TryGetCookie(string name, out string? value)
```

```csharp

/// <summary>
/// Enumerate all cookies as key/value pairs.
/// </summary>
public IEnumerable<KeyValuePair<string, string>> EnumerateCookies()
```

