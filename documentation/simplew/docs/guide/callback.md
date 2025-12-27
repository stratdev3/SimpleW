# Callback

SimpleW provides a small but powerful set of extension points that allow you to inject logic into the execution flow without modifying the routing or handler model.

These mechanisms are intentionally limited and explicit, to avoid hidden behavior.


## OnBeforeMethod

The `Controller` base class exposes the virtual method [`OnBeforeMethod()`](../reference/controller#onbeforemethod).

This method is executed **before every controller route method**.

Typical use cases include :
- Initializing controller state
- Access checks
- Per-request setup logic

```csharp
public class TestController : Controller {

    public override void OnBeforeMethod() {
        // executed before each route method
        // example: authentication check
    }

}
```

### Characteristics

- Executed once per request
- Runs after routing, before the handler method
- Applies only to controllers (not delegate-based handlers)

### Mental Model

> OnBeforeMethod() is a per-controller pre-execution hook.


## Controller Subclassing

For cross-cutting controller logic, subclassing [`Controller`](../reference/controller.md) is often the cleanest approach.

Instead of duplicating logic across multiple controllers, you can define a shared base class.

### Example

```csharp
public abstract class BaseController : Controller {

    protected void CheckSomething() {
        // shared logic
    }
}

public class UserController : BaseController {

    [Route("GET", "/users")]
    public object Users() {
        CheckSomething();
        return "ok";
    }
}
```

This approach keeps :
- Business logic explicit
- Controllers focused
- Shared behavior centralized


## Excluding Base Controllers from Routing

A base controller may itself define route methods.

If you do not want this class to be registered as a controller, you must explicitly exclude it when mapping controllers.

This avoids unintended routes being exposed.

```csharp
server.MapControllers<BaseController>("/", exclude: new[] { typeof(BaseController) });
```

### Shared Routes via Base Controllers

If a base controller **does define routes**, those routes are inherited by subclasses.

Example :

```csharp
public abstract class BaseController : Controller {

    [Route("GET", "/conf")]
    public object Conf() {
        return "shared config";
    }
}
```

Accessible as :
- `/api/user/conf`
- `/api/department/conf`

### Mental Model

> Subclassing shares behavior; routing decides visibility.


## Choosing the Right Mechanism

| Need	                         |   Recommended approach           |
|--------------------------------|----------------------------------|
| Per-request controller hook    |   OnBeforeMethod()               |
| Shared controller logic        |   Base controller subclass       |
| Cross-cutting request logic    |   Middleware                     |

Callbacks and subclassing complement middleware — they do not replace it.