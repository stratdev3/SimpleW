# SimpleWServer

The `SimpleWServer` is the main class used to instantiate and configure the web server.

This class exposes a **fluent API**, which means you can chain all its methods :

```csharp:line-numbers
// instanciate
var server = new SimpleWServer(IPAddress.Any, 2015);
// configure & run
await server.UseHttps(sslContext)
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

```csharp
/// <summary>
/// Initialize HTTP server with a given Unix domain socket endpoint
/// </summary>
/// <param name="endpoint">Unix domain socket endpoint</param>
public WsServer(UnixDomainSocketEndPoint endpoint)
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
    options.ReuseAddress = true;
    options.TcpNoDelay = true;
    options.TcpKeepAlive = true;
});
```

See [`SimpleWSServerOptions`](./simplewserveroptions.md) for more information on all options.


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
/// Is the server started?
/// </summary>
public bool IsStarted { get; private set; }
```

```csharp
/// <summary>
/// Is the server currently stopping?
/// </summary>
public bool IsStopping { get; private set; } = false;
```


## Router

The [`Router`](./router) property contains all routes handled by the web server.
You can list them with `Router.Routes`.


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
    return session.SendJsonAsync(new { message = "Hello World !" });
});
// can mixte the special HttpSession with any others parameters
server.MapGet("/api/test/hello", (HttpSession session, string? name = null) => {
    return session.SendJsonAsync(new { message = $"Hello {name} !" });
});
// the order of parameter does not matter, only its types does
server.MapGet("/api/test/hello", (string? name = null, HttpSession session) => {
    return session.SendJsonAsync(new { message = $"Hello {name} !" });
});
// handler can be async
server.MapGet("/api/test/hello", async (HttpSession session, string? name = null) => {
    await Task.Delay(2_000);
    await session.SendJsonAsync(new { message = $"Hello {name} !" });
});
// can mixte return anonymous object and strongly typed object
server.MapGet("/api/test/hello", object (HttpSession session, string? name = null) => {
    if (string.IsNullOrWhiteSpace(name)); {
        return Session.Response.Status(404).Text("you must set a name parameter");
    }
    return new { message = $"Hello {name} !" };
});
// can return ValueTask<object>
server.MapGet("/api/test/hello", async ValueTask<object> (HttpSession session, string? name = null) => {
    if (string.IsNullOrWhiteSpace(name)); {
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


## ConfigureHandlerResult

```csharp
/// <summary>
/// Override HandlerResult, Action to do for the non nulls returns
/// </summary>
/// <param name="handler"></param>
public SimpleWServer ConfigureHandlerResult(HttpHandlerResult handler)
```

See more [example](../guide/handlerresult.md).


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
public void UseModule(IHttpModule module)
```

See more [example](../guide/module.md).


## Controllers

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
/// Register all controllers assignable to TController found in the same assembly
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

Suppose you have [subclass](../guide/callback.md#subclass) the `Controller` class to add properties or methods of your own in a new `BaseController`. To call all your controllers based on you custome class : 

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.MapControllers<BaseController>("/api");
```

::: tip NOTE
The search for `Controller` based class occurs only once at **runtime using reflection**.<br />
However, `Router` use **compiled delegate**, close to hard-coded method calls, to execute code on each request.
:::


## JsonEngine


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
You can learn how to change the [`JsonEngine`](../guide/response.md#json-engine) for [Newtonsoft](https://www.nuget.org/packages/Newtonsoft.Json) using the [SimpleW.JsonEngine.Newtonsoft](https://www.nuget.org/packages/SimpleW.JsonEngine.Newtonsoft) nuget package.
:::


## SSL Certificate

```csharp
/// <summary>
/// Add SslContext
/// </summary>
/// <param name="sslContext"></param>
/// <returns></returns>
public SimpleWServer UseHttps(SslContext sslContext)
```

This methode takes an `SslContext` and use it to run an HTTPS server.

::: code-group

```csharp [NET8]
X509Certificate2 cert = new(@"certifcate.pfx", "password");
new SslContext(SslProtocols.Tls12 | SslProtocols.Tls13, cert);
```

```csharp [NET9 or above]
X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(@"certifcate.pfx", "password");
new SslContext(SslProtocols.Tls12 | SslProtocols.Tls13, cert);
```

:::

See an [example](../guide/ssl-certificate.md#example-for-local-test).


## ConfigureJwtResolver

```csharp
/// <summary>
/// Get the JwtResolver
/// </summary>
public JwtResolver JwtResolver
```

```csharp
/// <summary>
/// Configure the JwtResolver
/// </summary>
/// <param name="jwtResolver"></param>
/// <returns></returns>
public SimpleWServer ConfigureJwtResolver(JwtResolver jwtResolver)
```


## ConfigureUserResolver

```csharp
/// <summary>
/// Get the UserResolver
/// </summary>
internal WebUserResolver UserResolver { get; private set; } = WebUserResolvers.TokenWebUser;
```

This property defines the User Resolver used in handler. The default user resolver is TokenWebUser.

```csharp
/// <summary>
/// Configure the UserResolver
/// </summary>
/// <param name="userResolver"></param>
/// <returns></returns>
public SimpleWServer ConfigureUserResolver(WebUserResolver userResolver)
```


## Telemetry

```csharp
/// <summary>
/// Status of Telemetry
/// </summary>
public bool IsTelemetryEnabled
```

```csharp
/// <summary>
/// Enable Telemetry
/// </summary>
/// <returns></returns>
public SimpleWServer EnableTelemetry() {
    Telemetry.Enable();
    return this;
}
```

```csharp
/// <summary>
/// Disable Telemetry
/// </summary>
/// <returns></returns>
public SimpleWServer DisableTelemetry()
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
    options.IncludeStackTrace = true;
});
```

See [`TelemetryOptions`](./telemetryoptions.md) for more information on all options.


See an more [complete example](../guide/observability.md).
