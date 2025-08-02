# Controller

The `Controller` is the base class for REST API controllers


## GetToken()

```csharp
/// <summary>
/// Get the JWT by order :
///     1. Session.jwt (websocket only)
///     2. Request url querystring "jwt" (api only)
///     3. Request http header "Authorization: bearer " (api only)
/// </summary>
string GetJwt()
```

The `GetJwt()` is used to retrieved the raw string token by looking possible location.


## webuser

```csharp
/// <summary>
/// Get Current IWebUser
/// </summary>
IWebUser webuser
```

When using the integrated JWT authentification, this property is the current user implementing the `IWebUser` interface.



