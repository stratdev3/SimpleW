# HttpBag

This class allows middlewares, handlers, and controllers to share data during the lifetime of a single HTTP request.


```csharp
/// <summary>
/// Store or replace a value
/// </summary>
public void Set<T>(string key, T value)
```

```csharp
/// <summary>
/// Try get a typed value
/// Returns false when :
/// - key does not exist
/// - stored value type does not match T
/// - stored value is null and T is a non-nullable value type
/// </summary>
public bool TryGet<T>(string key, out T? value)
```

```csharp
/// <summary>
/// Get a typed value or throw if missing / invalid type
/// </summary>
public T Get<T>(string key)
```

```csharp
/// <summary>
/// Get a typed value or return defaultValue if missing / invalid type
/// </summary>
public T? GetOrDefault<T>(string key, T? defaultValue = default)
```

```csharp
/// <summary>
/// Remove a key
/// </summary>
public bool Remove(string key)
```

```csharp
/// <summary>
/// Remove all values
/// </summary>
public void Clear()
```
