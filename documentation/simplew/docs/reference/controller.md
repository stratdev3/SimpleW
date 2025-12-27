# Controller

The `Controller` is the base class for REST API controllers

It contains several properties and methods. The following one are the most used.


## Session

```csharp
/// <summary>
/// Gets the current HTTP Session
/// </summary>
public HttpSession Session;
```

This property contains the current Session instance. See the [HttpSession](./httpsession) class for uses.


## Request

```csharp
/// <summary>
/// Gets the current HTTP Request (alias to Session.Request)
/// </summary>
public HttpRequest Request;
```

This property contains all informations about the request. See the [HttpRequest](./httprequest) class for uses.


## Response

```csharp
/// <summary>
/// Gets the current prepared HTTP Response (alias to Session.Response)
/// </summary>
public HttpResponse Response;
```

This property can be used to return a response object which will be send to client.
It's initialized as an empty Response instance. See the [HttpResponse](./httpresponse) class for uses.


## OnBeforeMethod

This [callback](../guide/callback#onbeforemethod) is defined as

```csharp
/// <summary>
/// Override this Handler to call code before any Controller.Method()
/// </summary>
public virtual void OnBeforeMethod() { }
```
