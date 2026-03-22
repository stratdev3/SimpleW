# IdentityProperty

`IdentityProperty` represents a custom key/value pair attached to an identity.

It is used to store additional metadata such as tenant information, permissions, or any domain-specific data.


```csharp
/// <summary>
/// Constructor
/// </summary>
/// <param name="key"></param>
/// <param name="value"></param>
public IdentityProperty(string key, string value)
```

```csharp
/// <summary>
/// Key
/// </summary>
public string Key { get; }
```

```csharp
/// <summary>
/// Value
/// </summary>
public string Value { get; }
```
