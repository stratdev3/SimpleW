# Session

The `Session` property of type `SimpleWSession` or `SimpleWSSession`, both implementing the `ISimpleWSession` interface.


## Server

```csharp
/// <summary>
/// Server Instance
/// </summary>
public new ISimpleWServer Server;
```

This property can be used to control Server from any `Controller` class.


## jwt

```csharp
/// <summary>
/// JWT
/// </summary>
public string jwt { get; set; }
```

## webuser

```csharp
/// <summary>
/// <para>Get Current IWebUser</para>
/// <para>set by the underlying Controller.webuser
///       The only use case to have a webuser
///       property here is for logging</para>
/// </summary>
public IWebUser webuser { get; set; }
```
