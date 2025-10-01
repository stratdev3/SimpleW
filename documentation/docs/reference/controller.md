# Controller

The `Controller` is the base class for REST API controllers

It contains many properties and methods. The following one are the most used.


## Request

```csharp
/// <summary>
/// Gets the current HTTP Request
/// </summary>
protected HttpRequest Request;
```

This property contains all informations about the request. See the [HttpRequest](./httprequest) class for uses.


## Response

```csharp
/// <summary>
/// Gets the current prepared HTTP Response
/// </summary>
protected HttpResponse Response;
```

This property can be used to return a response object which will be send to client.
It's initialized as an empty Response instance. See the [HttpResponse](./httpresponse) class for uses.


## Session

```csharp
/// <summary>
/// Gets the current HTTP Session
/// </summary>
protected ISimpleWSession Session;
```

This property contains the current Session instance. See the [ISimpleWSession](./isimplewsession) class for uses.


## OnBeforeMethod

This [callback](../guide/api-callback#onbeforemethod) is defined as

```csharp
/// <summary>
/// Override this Handler to call code before any Controller.Method()
/// </summary>
public virtual void OnBeforeMethod() { }
```


## GetToken()

```csharp
/// <summary>
/// Get the JWT by order :
///     1. Session.jwt (websocket only)
///     2. Request url querystring "jwt" (api only)
///     3. Request http header "Authorization: bearer " (api only)
/// </summary>
public string GetJwt()
```

The `GetJwt()` is used to retrieved the raw string token by looking possible location.


## webuser

```csharp
/// <summary>
/// Get Current IWebUser
/// </summary>
protected IWebUser webuser { get; }
```

When using the integrated JWT authentification, this property is the current user implementing the `IWebUser` interface.


## MakeAccessResponse()

```csharp
/// <summary>
/// Make Error Access response
/// </summary>
public HttpResponse MakeAccessResponse()
```

The `MakeAccessResponse()` will create 401 or 403 response error code depending the status or [`webuser`](./controller#webuser) property.


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
