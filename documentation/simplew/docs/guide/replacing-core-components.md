# Replacing Core Components

SimpleW provides default implementations for its network engine, router, controller action executor factory, and JSON engine.
These components can be configured or replaced when you need behavior that differs from the defaults.


## Network Engine

By default, `SimpleWServer` uses `SimpleWEngine` as its network engine.

### Configure the default Network Engine

Configure the default socket engine with `UseEngine(Action<SimpleWEngineOptions>)` before starting the server.

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseEngine(options => {
    options.TcpNoDelay = true;
    options.ReuseAddress = true;
    options.TcpKeepAlive = true;
});
```

### Replace the default Network Engine

If you need a custom transport/listener implementation, replace the default engine with `UseEngine(ISimpleWEngine)`.

```csharp
server.UseEngine(new MyCustomEngine());
```

Your custom engine only needs to implement [`ISimpleWEngine`](../reference/isimplewengine.md). The engine owns its accept loop; for each accepted connection, wrap it as an `ISimpleWEngine` and call `server.CreateSessionAsync(...)`.

Typical use cases :
- Customize how incoming connections are accepted
- Experiment with another low-level listener implementation
- Extend the default engine behavior in a dedicated class


### Available Network Engines

See [Addons](../addons/addons.md) to find all the available Network engines.
The most performant is [ioxide](../addons/engine-ioxide.md).


## Replace Router

By default, SimpleW uses the built-in `Router` implementation.

In advanced scenarios, you can replace the router with a custom implementation.

```csharp
server.UseRouter(new MyCustomRouter());
```

::: info
When the router is replaced, any registered route is lost.
:::

Typical use cases :
- Implement a custom routing strategy
- Wrap the default router to add cross-cutting features
- Experiment with alternative routing pipelines


## Replace Controller Action Executor Factory

The `ControllerActionExecutorFactory` creates the executor used for each controller action.
It receives the controller type and action method, then returns the `HttpRouteExecutor` that handles requests for that action.

Replace the default factory with [`UseControllerActionExecutorFactory(...)`](../reference/simplewserver.md#usecontrolleractionexecutorfactory) :

```csharp
server.UseControllerActionExecutorFactory((controllerType, method) => {
    return (session, resultHandler) => {
        return resultHandler(session, new {
            Controller = controllerType.Name,
            Action = method.Name
        });
    };
});

server.MapControllers<Controller>();
```

In this example, every mapped controller action is replaced with an executor that returns the controller and action names without invoking the original method.

The factory is called once when an action is mapped. The returned executor is then reused for every request matching that route.

::: warning
Call `UseControllerActionExecutorFactory(...)` before `MapController(...)` or `MapControllers(...)`, and before starting the server. Routes that are already mapped keep their existing executors.
:::

A custom factory is responsible for the complete controller execution pipeline, including controller creation, session assignment, parameter binding, method invocation, return-value handling, and controller disposal when required.

Typical use cases :
- Resolve controllers through a dependency injection container
- Customize action parameter binding or controller lifecycle
- Add specialized instrumentation around controller execution

Example : the official [Dependency Injection helper](../addons/helper-dependency-injection.md) uses this extension point to resolve controllers from `Microsoft.Extensions.DependencyInjection`.


## Replace JSON Engine

The JSON Engine defines how SimpleW serializes, deserializes, and populates objects when handling HTTP requests and responses.

It is exposed through the [`JsonEngine`](../reference/simplewserver.md#jsonengine) property and is used consistently by :
- Handlers returning objects
- `HttpResponse.Json(...)`
- `Request.BodyMap(...)`
- Model population and deserialization helpers

Conceptually :

> The JSON Engine is the single source of truth for JSON behavior in SimpleW.

### Default JSON Engine

By default, SimpleW uses `System.Text.Json`, configured with recommended, performance-oriented options.

This default engine provides :
- High performance
- Low allocations
- Native integration with .NET
- Sensible defaults for most APIs

For the majority of use cases, no configuration is required.

### Write your own JSON Engine

If neither `System.Text.Json` nor `Newtonsoft.Json` fits your needs, you can implement your own engine.

Simply implement the [`IJsonEngine`](../reference/ijsonengine.md) interface and register it :

```csharp
server.ConfigureJsonEngine(new MyCustomJsonEngine());
```

This makes it possible to :
- Plug in a custom serializer
- Wrap an existing JSON library
- Apply project-specific conventions

### Available JSON Engines

See [Addons](../addons/addons.md) to find all the available JSON engines.
The most famous, `Newtonsoft`, has its [wrapper](../addons/jsonengine-newtonsoft.md).
