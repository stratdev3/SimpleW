# Controller

The `Controller` is the base class for REST API controllers

It contains several properties and methods. The following ones are the most used.


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

This property contains all the information about the request. See the [HttpRequest](./httprequest) class for uses.


## Response

```csharp
/// <summary>
/// Gets the current prepared HTTP Response (alias to Session.Response)
/// </summary>
public HttpResponse Response;
```

This property can be used to return a response object that will be sent to the client.
It's initialized as an empty Response instance. See the [HttpResponse](./httpresponse) class for uses.


## Principal

```csharp
/// <summary>
/// Gets the current resolved user.
/// </summary>
public HttpPrincipal Principal => Session.Principal;
```

This property can be used to define security. See the [HttpPrincipal](./httpprincipal.md) class for uses.


## Bag

```csharp
/// <summary>
/// Gets the current HttpBag
/// </summary>
public HttpBag Bag => Session.Bag;
```

This property can be used to pass data between middleware. See the [HttpBag](./httpbag.md) class for uses.


## Metadata

```csharp
/// <summary>
/// Gets the metadata attached to the matched handler.
/// </summary>
public HandlerMetadataCollection Metadata
```

See the [HandlerMetadataCollection](../guide/handler-attribute.md) class for uses.


## OnBeforeMethod

This [callback](../guide/callback#onbeforemethod) is defined as

```csharp
/// <summary>
/// Override this Handler to call code before any Controller.Method()
/// </summary>
public virtual void OnBeforeMethod() { }
```
