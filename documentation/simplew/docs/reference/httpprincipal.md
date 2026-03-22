# HttpPrincipal

`HttpPrincipal` represents the current user making the request.

It provides a high-level access layer over the underlying identity and exposes helper methods to query roles and properties.


```csharp
/// <summary>
/// Identity
/// </summary>
public HttpIdentity Identity { get; }
```

```csharp
/// <summary>
/// IsAuthenticated
/// </summary>
public bool IsAuthenticated
```

```csharp
/// <summary>
/// Name
/// </summary>
public string? Name
```

```csharp
/// <summary>
/// Email
/// </summary>
public string? Email
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
/// IsInRoles
/// </summary>
/// <param name="roles"></param>
/// <returns></returns>
public bool IsInRoles(string roles)
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
