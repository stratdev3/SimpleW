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


## Socket Options

```csharp
/// <summary>
/// This option will set the maximum length of the pending connections queue.
/// </summary>
public int OptionListenSocketBacklog { get; set; } = 1024;
```

```csharp
/// <summary>
/// Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
/// Will work only if socket is bound on IPv6 address.
/// </summary>
public bool OptionDualMode { get; set; }
```

```csharp
/// <summary>
/// This option will enable/disable Nagle's algorithm for TCP protocol
/// </summary>
public bool OptionNoDelay { get; set; }
```

```csharp
/// <summary>
/// This option will enable/disable SO_REUSEADDR if the OS support this feature
/// </summary>
public bool OptionReuseAddress { get; set; }
```

```csharp
/// <summary>
/// This option will enable/disable SO_EXCLUSIVEADDRUSE if the OS support this feature
/// </summary>
public bool OptionExclusiveAddressUse { get; set; }
```

```csharp
/// <summary>
/// This option will enable SO_REUSEPORT if the OS support this feature (linux only)
/// </summary>
public bool OptionReusePort { get; set; }
```

```csharp
/// <summary>
/// This option will run the accept socket on each machine's core
/// </summary>
public bool OptionRunAcceptSocketPerCore { get; set; }
```

```csharp
/// <summary>
/// This option will setup SO_KEEPALIVE if the OS support this feature
/// </summary>
public bool OptionKeepAlive { get; set; }
```

```csharp
/// <summary>
/// The number of seconds a TCP connection will remain alive/idle before keepalive probes are sent to the remote
/// </summary>
public int OptionTcpKeepAliveTime { get; set; } = -1;
```

```csharp
/// <summary>
/// The number of seconds a TCP connection will wait for a keepalive response before sending another keepalive probe
/// </summary>
public int OptionTcpKeepAliveInterval { get; set; } = -1;
```

```csharp
/// <summary>
/// The number of TCP keep alive probes that will be sent before the connection is terminated
/// </summary>
public int OptionTcpKeepAliveRetryCount { get; set; } = -1;
```

```csharp
/// <summary>
/// Option: receive buffer size
/// </summary>
public int OptionReceiveBufferSize { get; set; } = 16 * 1024;
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


## Router

The [`Router`](./router) property contains all routes handled by the web server.
You can list them with `Router.Routes`.


## Map

For a rapid prototyping, `Delegate` can be set inline.

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


## UseHandlerResult

```csharp
/// <summary>
/// Override HandlerResult, Action to do for the non nulls returns
/// </summary>
/// <param name="handler"></param>
public SimpleWServer UseHandlerResult(HttpHandlerResult handler)
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
public SimpleWServer UseController<TController>(string? basePrefix = null) where TController : Controller
```

This method will integrate the class in the `Router` as a REST API under the `basePrefix` endpoint.


#### Example

You have `UserController`, `DepartmentController` and `WorkController` classes 
and you want them to be served by the web server under the `/api` endpoint.

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.UseController<UserController>("/api");
server.UseController<DepartmentController>("/api");
server.UseController<WorkController>("/api");
```

::: tip NOTE
You need to add a line for each `Controller` you want to integrate. 
This is a repetitive task and you can miss a thing. You should prefer the automatic
`UseControllers()` method to integrate all Controllers.
:::


### Automatic

```csharp
/// <summary>
/// Register all controllers assignable to TController found in the same assembly
/// </summary>
/// <typeparam name="TController"></typeparam>
/// <param name="basePrefix"></param>
/// <param name="excludes"></param>
public SimpleWServer UseControllers<TController>(string? basePrefix = null, IEnumerable<Type>? excludes = null) where TController : Controller
```

At runtime, this method will find all classes based on `TController` class and integrate them in the `Router` as a REST API under the `basePrefix` endpoint.


#### Example

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.UseControllers<Controller>("/api");
```

The parameter `excludes` can be useful if you want to exclude some Controller from being integrated.


#### Example

Suppose you have a `MaintenanceController` class which must only be used when you want to make the web server in a special maintenance mode.

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.UseControllers<Controller>("/api", new Type[] { typeof(MaintenanceController) });
```

#### Example

Suppose you have [subclass](../guide/api-callback.md#subclass) the `Controller` class to add properties or methods of your own in a new `BaseController`. To call all your controllers based on you custome class : 

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.UseControllers<BaseController>("/api");
```

::: tip NOTE
The search for `Controller` based class occurs only once at **runtime using reflection**.<br />
However, `Router` use **compiled delegate**, close to hard-coded method calls, to execute code on each request.
:::


### SetToken()

```csharp
/// <summary>
/// Set Token settings (passphrase and issuer).
/// a delegate can be defined to redress webuser called by Controller.JwtToWebUser().
/// </summary>
/// <param name="tokenPassphrase">The String token secret passphrase (min 17 chars).</param>
/// <param name="issuer">The String issuer.</param>
/// <param name="getWebUserCallback">The DelegateSetTokenWebUser getWebUserCallback</param>
void SetToken(string tokenPassphrase, string issuer, DelegateSetTokenWebUser getWebUserCallback = null)
```


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
public SimpleWServer UseJsonEngine(IJsonEngine jsonEngine)
```

To change the engine just provide an object which implement the [`IJsonEngine`](./ijsonengine.md) interface.


::: tip NOTE
You can learn how to change the [`JsonEngine`](../guide/api-response.md#json-engine) for [Newtonsoft](https://www.nuget.org/packages/Newtonsoft.Json) using the [SimpleW.Newtonsoft](https://www.nuget.org/packages/SimpleW.Newtonsoft) nuget package.
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


## Security

```csharp
/// <summary>
/// Max size of request headers in bytes (default: 64 KB)
/// </summary>
public int OptionMaxRequestHeaderSize { get; set; } = 64 * 1024;
```

```csharp
/// <summary>
/// Max size of request body in bytes (default: 10 MB)
/// </summary>
public long OptionMaxRequestBodySize { get; set; } = 10 * 1024 * 1024;
```

```csharp
/// <summary>
/// True to allow some headers as source of truth for Telemetry
/// Example : X-Forwarded-Host, X-Real-IP (...) are often used to pass data
///           from reverse proxy (nginx...) to upstream server.
/// Note : you should allow only if you have a reverse proxy with well defined settings policy
/// </summary>
public bool TrustXHeaders { get; set; } = false;
```


## Telemetry

```csharp
/// <summary>
/// True to generate Telemetry Traces, Logs and Metrics
/// </summary>
public bool EnableTelemetry { get; set; } = false;
```
