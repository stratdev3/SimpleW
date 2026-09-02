# Controller

The `Controller` is the base class for REST API controllers

Every controller must inherit from `Controller`.
Public instance methods decorated with a [`Route`](./routeattribute.md) attribute can then be registered by [`MapController<T>()`](./simplewserver.md#mapcontrollers) or [`MapControllers<T>()`](./simplewserver.md#mapcontrollers).


## Lifecycle and construction

By default, SimpleW creates a **new controller instance for every handler execution**.

In most cases, no constructor needs to be declared:

```csharp
public class UserController : Controller {

    [Route("GET", "/users")]
    public object List() {
        return new[] { "Alice", "Bob" };
    }
}
```

C# automatically provides a public parameterless constructor when the class does not declare any constructor.

For constructor injection, use the [Dependency Injection helper](../addons/helper-dependency-injection.md), which replaces the default controller action executor factory.
A controller instance is scoped to one handler execution. Do not use controller instance fields to store application-wide or cross-request state.


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

This [callback](../guide/controller-callback#onbeforemethod) is defined as

```csharp
/// <summary>
/// Override this Handler to call code before any Controller.Method()
/// </summary>
public virtual void OnBeforeMethod() { }
```
