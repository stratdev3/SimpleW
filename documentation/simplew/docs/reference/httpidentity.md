# HttpIdentity

`HttpIdentity` represents how a user is identified and authenticated within a request.

It contains core identity data such as identifier, name, email, roles, and custom properties, and is the foundation of the Principal system.


```csharp
/// <summary>
/// Constructor
/// </summary>
/// <param name="isAuthenticated"></param>
/// <param name="authenticationType"></param>
/// <param name="identifier"></param>
/// <param name="name"></param>
/// <param name="email"></param>
/// <param name="roles"></param>
/// <param name="properties"></param>
public HttpIdentity(
    bool isAuthenticated,
    string? authenticationType,
    string? identifier,
    string? name,
    string? email,
    IEnumerable<string>? roles,
    IEnumerable<IdentityProperty>? properties
)
```

```csharp
/// <summary>
/// IsAuthenticated
/// </summary>
public bool IsAuthenticated { get; }
```

```csharp
/// <summary>
/// AuthenticationType
/// </summary>
public string? AuthenticationType { get; }
```

```csharp
/// <summary>
/// Id
/// </summary>
public string? Identifier { get; }
```

```csharp
/// <summary>
/// Name
/// </summary>
public string? Name { get; }
```

```csharp
/// <summary>
/// Email
/// </summary>
public string? Email { get; }
```

```csharp
/// <summary>
/// IsInRole
/// </summary>
/// <param name="role"></param>
/// <returns></returns>
public bool IsInRole(string role)
```

```csharp
/// <summary>
/// Get a property
/// </summary>
/// <param name="key"></param>
/// <returns></returns>
public string? Get(string key)
```

```csharp
/// <summary>
/// Has
/// </summary>
/// <param name="key"></param>
/// <param name="value"></param>
/// <returns></returns>
public bool Has(string key, string? value = null)
```
