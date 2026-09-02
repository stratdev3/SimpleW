# SimpleWServer

The `SimpleWServer` is the main class used to instantiate and configure the web server.

This class exposes a **fluent API**, which means you can chain all its methods :

```csharp
// instanciate
var server = new SimpleWServer(IPAddress.Any, 2015);
// configure & run
await server.UseEngine(options => {
                options.SslContext = sslContext;
            })
            .MapGet("/api/hello/world", () => {
                return new { message = "Hello World !" };
            })
            .RunAsync();
```


## Constructor

```csharp
/// <summary>
/// Initialize server with a given IP address and port number
/// </summary>
/// <param name="address"></param>
/// <param name="port"></param>
public SimpleWServer(IPAddress address, int port)
```

```csharp
/// <summary>
/// Initialize WebSocket server with a given DNS endpoint
/// </summary>
/// <param name="endpoint">DNS endpoint</param>
public SimpleWServer(DnsEndPoint endpoint)
```

```csharp
/// <summary>
/// Initialize WebSocket server with a given IP endpoint
/// </summary>
/// <param name="endpoint">IP endpoint</param>
public SimpleWServer(IPEndPoint endpoint)
```


## Endpoint

```csharp
/// <summary>
/// Use IPAddress
/// </summary>
/// <param name="address"></param>
/// <returns></returns>
/// <exception cref="InvalidOperationException"></exception>
public SimpleWServer UseAddress(IPAddress address)
```

```csharp
/// <summary>
/// Use Port
/// </summary>
/// <param name="port"></param>
/// <returns></returns>
public SimpleWServer UsePort(int port)
```


## Configure

```csharp
/// <summary>
/// Configure
/// </summary>
/// <param name="configure"></param>
/// <returns></returns>
public SimpleWServer Configure(Action<SimpleWSServerOptions> configure)
```

Example :

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);
server.Configure(options => {
    options.MaxRequestBodySize = 100 * 1024 * 1024;
    options.SessionTimeout = TimeSpan.FromSeconds(30);
});
```

See [`SimpleWSServerOptions`](./simplewserveroptions.md) for server options and [`SimpleWEngineOptions`](./simplewengineoptions.md) for default engine options.


## Control

```csharp
/// <summary>
/// Start the server (not blocking)
/// </summary>
/// <param name="cancellationToken"></param>
public Task StartAsync(CancellationToken cancellationToken = default)
```

```csharp
/// <summary>
/// Run the server (blocking)
/// </summary>
/// <param name="cancellationToken"></param>
public async Task RunAsync(CancellationToken cancellationToken = default)
```

```csharp
/// <summary>
/// Stop Server
/// </summary>
public async Task StopAsync()
```

```csharp
/// <summary>
/// Reload the listener socket without stopping the server lifetime.
/// Does NOT close existing client sessions; only affects new incoming connections.
/// </summary>
public async Task ReloadListenerAsync(Action<SimpleWServer> reconfigure, CancellationToken cancellationToken = default)
```

When the reload fails after it has started, the method attempts to restore the previous listener and throws `ListenerReloadException`:

```csharp
public sealed class ListenerReloadException : Exception {
    public Exception ReloadException { get; }
    public Exception? RollbackException { get; }
    public bool ListenerRestored { get; }
}
```

| Property | Meaning |
| --- | --- |
| `ReloadException` | Original exception raised by the reload. |
| `RollbackException` | Exception raised while restoring the previous listener, or `null` when restoration succeeded. |
| `ListenerRestored` | `true` when the previous listener is active again; otherwise `false`. |

`ListenerReloadException.InnerException` references `ReloadException`. A successful rollback does not turn the requested reload into a successful operation.

## State

`State` exposes a thread-safe snapshot of the server lifecycle. The former `IsStarted`, `IsStopping`, and `IsListenerReloading` properties have been removed.

```csharp
public enum SimpleWServerState {
    Stopped,
    Starting,
    Started,
    Reloading,
    Stopping,
    Faulted
}

public SimpleWServerState State { get; }
```

| State | Meaning |
| --- | --- |
| `Stopped` | The server is not running and can be configured or started. |
| `Starting` | The engine and runtime services are starting. |
| `Started` | The server is running normally. |
| `Reloading` | The listener is being replaced or restored. |
| `Stopping` | The engine and local resources are being stopped. |
| `Faulted` | A start cleanup, stop, or reload rollback failed. Call `StopAsync()` to retry cleanup. |

Lifecycle operations are serialized. A concurrent operation waits for the current transition, then applies its intent to the resulting state. `StartAsync()` and `ReloadListenerAsync()` reject `Faulted`; a successful recovery through `StopAsync()` returns the server to `Stopped`.

## OnStateChanged

You can subscribe to every real server state transition. Subscribing does not immediately emit the current state.

```csharp
/// <summary>
/// Register a callback invoked after each server state transition.
/// </summary>
public SimpleWServer OnStateChanged(Action<SimpleWServer, SimpleWServerState> callback);

/// <summary>
/// Register an async callback invoked after each server state transition.
/// </summary>
public SimpleWServer OnStateChanged(Func<SimpleWServer, SimpleWServerState, Task> callback);
```

Callbacks run sequentially in registration order. Async callbacks are awaited before the transition completes. Exceptions are logged and isolated so later subscribers and the lifecycle operation continue. Lifecycle methods cannot be called from a state callback.

See an [example](../guide/server.md#lifecycle-state-callbacks).


## Map

For a rapid prototyping, [`handler`](../guide/handler.md) can be set inline.

```csharp
/// <summary>
/// Add handler for Method/Path
/// </summary>
/// <param name="method"></param>
/// <param name="path"></param>
/// <param name="handler"></param>
/// <returns></returns>
public SimpleWServer Map(string method, string path, Delegate handler)
```

```csharp
/// <summary>
/// Add handler for GET request
/// alias for Map("GET", path, handler)
/// </summary>
/// <param name="path"></param>
/// <param name="handler"></param>
public SimpleWServer MapGet(string path, Delegate handler)
```

```csharp
/// <summary>
/// Add handler for POST request
/// alias for Map("POST", path, handler)
/// </summary>
/// <param name="path"></param>
/// <param name="handler"></param>
public SimpleWServer MapPost(string path, Delegate handler)
```

The following examples shows how to set a delegate handler.

```csharp
// no parameter
server.MapGet("/api/test", () => {
    return new { message = "Hello World !" };
});
// optional parameter "name" : if any query string with key "name" exists, it will map the value into name
server.MapGet("/api/test/hello", (string? name = null) => {
    return new { message = $"Hello {name} !" };
});
// required parameter "name" : a query string with key "name" must exists and it will map the value into name. else 500 error
server.MapGet("/api/test/hello", (string name) => {
    return new { message = $"Hello {name} !" };
});
// required parameter "name" : it will map url path part to the value into name
server.MapGet("/api/test/:name", (string? name = null) => {
    return new { message = $"Hello {name} !" };
});
// retrieve the underlying Session object (will be inject)
server.MapGet("/api/test/hello", (HttpSession session) => {
    return session.Response.Json(new { message = "Hello World !" });
});
// can mixte the special HttpSession with any others parameters
server.MapGet("/api/test/hello", (HttpSession session, string? name = null) => {
    return session.Response.Json(new { message = $"Hello {name} !" });
});
// the order of parameter does not matter, only its types does
server.MapGet("/api/test/hello", (string? name = null, HttpSession session) => {
    return session.Response.Json(new { message = $"Hello {name} !" });
});
// handler can be async
server.MapGet("/api/test/hello", async (HttpSession session, string? name = null) => {
    await Task.Delay(2_000);
    await session.Response.Json(new { message = $"Hello {name} !" });
});
// can mixte return anonymous object and strongly typed object
server.MapGet("/api/test/hello", object (HttpSession session, string? name = null) => {
    if (string.IsNullOrWhiteSpace(name)) {
        return Session.Response.Status(404).Text("you must set a name parameter");
    }
    return new { message = $"Hello {name} !" };
});
// can return ValueTask<object>
server.MapGet("/api/test/hello", async ValueTask<object> (HttpSession session, string? name = null) => {
    if (string.IsNullOrWhiteSpace(name)) {
        await Task.Delay(2_000);
        return Session.Response.Status(404).Text("you must set a name parameter");
    }
    return new { message = $"Hello {name} !" };
});


```
::: tip NOTE
The handler can take multiple types of parameters, no order required :
  - query string parameters
  - route path parameters
  - special type [`HttpSession`](./httpsession.md) can be mapped inside the delegate.
:::


## MapControllers

### Manual

```csharp
/// <summary>
/// Register a Controller type and map all its routes
/// </summary>
/// <typeparam name="TController"></typeparam>
/// <param name="basePrefix">Optional base prefix like "/api". Can be null or empty.</param>
/// <returns></returns>
public SimpleWServer MapController<TController>(string? basePrefix = null) where TController : Controller
```

This method will integrate the class in the `Router` as a REST API under the `basePrefix` endpoint.


#### Example

You have `UserController`, `DepartmentController` and `WorkController` classes 
and you want them to be served by the web server under the `/api` endpoint.

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.MapController<UserController>("/api");
server.MapController<DepartmentController>("/api");
server.MapController<WorkController>("/api");
```

::: tip NOTE
You need to add a line for each `Controller` you want to integrate. 
This is a repetitive task and you can miss a thing. You should prefer the automatic
`MapControllers()` method to integrate all Controllers.
:::


### Automatic

```csharp
/// <summary>
/// Register all controllers assignable to TController found in the same appdomain
/// </summary>
/// <typeparam name="TController"></typeparam>
/// <param name="basePrefix"></param>
/// <param name="excludes"></param>
public SimpleWServer MapControllers<TController>(string? basePrefix = null, IEnumerable<Type>? excludes = null) where TController : Controller
```

At runtime, this method will find all classes based on `TController` class and integrate them in the `Router` as a REST API under the `basePrefix` endpoint.


#### Example

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.MapControllers<Controller>("/api");
```

The parameter `excludes` can be useful if you want to exclude some Controller from being integrated.


#### Example

Suppose you have a `MaintenanceController` class which must only be used when you want to make the web server in a special maintenance mode.

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.MapControllers<Controller>("/api", new Type[] { typeof(MaintenanceController) });
```

#### Example

Suppose you have [subclass](../guide/controller-callback.md#controller-subclassing) the `Controller` class to add properties or methods of your own in a new `BaseController`. To call all your controllers based on you custom class :

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.MapControllers<BaseController>("/api");
```

::: tip NOTE
The search for `Controller` based class occurs only once at **runtime using reflection**.<br />
However, `Router` use **compiled delegate**, close to hard-coded method calls, to execute code on each request.
:::


## UseEngine

```csharp
/// <summary>
/// Network engine used by the server.
/// </summary>
public ISimpleWEngine Engine { get; private set; }
```

By default, `SimpleWServer` uses `SimpleWEngine`.

```csharp
/// <summary>
/// Replace the current network engine implementation.
/// Must be called before the server starts.
/// </summary>
/// <param name="engine"></param>
/// <returns></returns>
public SimpleWServer UseEngine(ISimpleWEngine engine)
```

Use `UseEngine(Action<SimpleWEngineOptions>)` to configure the default socket engine.

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);
server.UseEngine(options => {
    options.ReuseAddress = true;
    options.TcpNoDelay = true;
    options.TcpKeepAlive = true;
});
```

Use `UseEngine(ISimpleWEngine)` when you want to replace the default network engine with your own implementation.

```csharp
server.UseEngine(new MyCustomEngine());
```

The custom engine must implement [`ISimpleWEngine`](../reference/isimplewengine.md).


## UseRouter

```csharp
/// <summary>
/// Replace the current router implementation.
/// Must be called before the server starts.
/// </summary>
public SimpleWServer UseRouter(IRouter router) {
    ArgumentNullException.ThrowIfNull(router);

    lock (_stateLock) {
        EnsureStateStopped("Router must be configured before starting the server.");
        Router = router;
    }
    return this;
}
```

```csharp
/// <summary>
/// Router
/// </summary>
public IRouter Router { get; private set; }
```

The [`Router`](./router) property contains all routes handled by the web server.
You can list them with `Router.Routes`.


## UseControllerActionExecutorFactory

```csharp
/// <summary>
/// Replaces the factory used to create controller action executors.
/// Must be called before the server starts.
/// </summary>
/// <param name="factory"></param>
/// <returns></returns>
public SimpleWServer UseControllerActionExecutorFactory(ControllerActionExecutorFactory factory)
```


## UseMiddleware

```csharp
/// <summary>
/// Add a new Middleware
/// </summary>
/// <param name="middleware"></param>
public void UseMiddleware(HttpMiddleware middleware)
```

See more [example](../guide/middleware.md).

## UseModule

```csharp
/// <summary>
/// Add a new Module
/// </summary>
/// <param name="module"></param>
/// <exception cref="ArgumentNullException"></exception>
/// <exception cref="InvalidOperationException">The server is not stopped.</exception>
public void UseModule(IHttpModule module)
```

`UseModule()` is the supported module installation entry point. It invokes `IHttpModule.Install()` only while `State == SimpleWServerState.Stopped`; module implementations should not repeat this validation.

See more [example](../guide/module.md).


## ConfigureJsonEngine


```csharp
/// <summary>
/// Get the current Json Serializer/Deserializer
/// </summary>
public IJsonEngine JsonEngine { get; private set; } = new SystemTextJsonEngine(SystemTextJsonEngine.OptionsSimpleWBuilder());
```

This property defines the Json engine used in server and controllers to serialize, deserialize and populate objects.
The default engine is `System.Text.Json` initialized with recommended options.


```csharp
/// <summary>
/// Set the Json Serializer/Deserializer
/// </summary>
/// <param name="jsonEngine"></param>
/// <returns></returns>
public SimpleWServer ConfigureJsonEngine(IJsonEngine jsonEngine)
```

To change the engine just provide an object which implement the [`IJsonEngine`](./ijsonengine.md) interface.


::: tip NOTE
You can learn how to replace the [`JsonEngine`](../guide/replacing-core-components.md#json-engine) for [Newtonsoft](https://www.nuget.org/packages/Newtonsoft.Json) using the [SimpleW.JsonEngine.Newtonsoft](https://www.nuget.org/packages/SimpleW.JsonEngine.Newtonsoft) nuget package.
:::


## ConfigureResultHandler

```csharp
/// <summary>
/// Override ResultHandler, Action to do for the non nulls returns
/// </summary>
/// <param name="handler"></param>
public SimpleWServer ConfigureResultHandler(HttpResultHandler handler)
```

See more [example](../guide/resulthandler.md).


## ConfigurePrincipalResolver

```csharp
/// <summary>
/// Resolve the current principal for each request.
/// Return null to use HttpPrincipal.Anonymous.
/// </summary>
public HttpPrincipalResolver? PrincipalResolver { get; private set; }
```

```csharp
/// <summary>
/// Configure the principal resolver.
/// </summary>
public SimpleWServer ConfigurePrincipalResolver(HttpPrincipalResolver resolver)
```

See an [example](../guide/principal.md).


## ConfigureClientIPResolver

```csharp
/// <summary>
/// Client IP Resolver
/// </summary>
internal Func<HttpSession, IPAddress?> ClientIpResolver { get; private set; } = (session) => {
    if (session.RemoteEndPoint is not IPEndPoint ep) {
        return null;
    }
    return ep.Address;
}
```

This property defines the Client IPAddress Resolver. The default client IPAddress resolver is the current `HttpSession.RemoteEndPoint.Address`.

```csharp
/// <summary>
/// Configure the ClientIPResolver
/// </summary>
/// <param name="clientIPResolver"></param>
/// <returns></returns>
public SimpleWServer ConfigureClientIPResolver(Func<HttpSession, IPAddress?> clientIPResolver)
```

See an [example](../guide/observability.md#custom-client-ip-resolution).


## Telemetry

```csharp
/// <summary>
/// Status of Telemetry
/// </summary>
public bool IsTelemetryEnabled
```

```csharp
/// <summary>
/// Configure Telemetry
/// </summary>
/// <param name="configure"></param>
/// <returns></returns>
public SimpleWServer ConfigureTelemetry(Action<TelemetryOptions> configure)
```

Example :

```csharp
server.ConfigureTelemetry(options => {
    options.Enabled = true;
    options.IncludeStackTrace = true;
});
```

Telemetry is created automatically when the server starts and disposed when it stops. It is recreated with the same options when the server restarts.

See [`TelemetryOptions`](./telemetryoptions.md) for more information on all options.


See an more [complete example](../guide/observability.md).
