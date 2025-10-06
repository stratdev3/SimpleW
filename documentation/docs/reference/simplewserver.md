# Server

The `SimplewServer` is the main class to instanciate and manipulate the web server

## Constructors

### HTTP

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

### HTTPS

```csharp
/// <summary>
/// Initialize WebSocket server with a given IP address and port number
/// </summary>
/// <param name="context">SSL context</param>
/// <param name="address">IP address</param>
/// <param name="port">Port number</param>
public SimpleWSServer(SslContext context, IPAddress address, int port)
```

```csharp
/// <summary>
/// Initialize WebSocket server with a given DNS endpoint
/// </summary>
/// <param name="context">SSL context</param>
/// <param name="endpoint">DNS endpoint</param>
public SimpleWSServer(SslContext context, DnsEndPoint endpoint)
```

```csharp
/// <summary>
/// Initialize WebSocket server with a given IP endpoint
/// </summary>
/// <param name="context">SSL context</param>
/// <param name="endpoint">IP endpoint</param>
public SimpleWSServer(SslContext context, IPEndPoint endpoint)
```

The class `SimpleWSServer` is exactly the same as `SimplewServer` except it takes a `SslContext` as primary argument.
This context is to use SSL Certificate.

```csharp
new SslContext(SslProtocols.Tls12, new X509Certificate2(@"certifcate.pfx", "password"));
```

## Socket Options

```csharp
/// <summary>
/// Option: acceptor backlog size
/// </summary>
/// <remarks>
/// This option will set the listening socket's backlog size
/// </remarks>
public int OptionAcceptorBacklog { get; set; } = 1024;
```

```csharp
/// <summary>
/// Option: dual mode socket
/// </summary>
/// <remarks>
/// Specifies whether the Socket is a dual-mode socket used for both IPv4 and IPv6.
/// Will work only if socket is bound on IPv6 address.
/// </remarks>
public bool OptionDualMode { get; set; }
```

```csharp
/// <summary>
/// Option: keep alive
/// </summary>
/// <remarks>
/// This option will setup SO_KEEPALIVE if the OS support this feature
/// </remarks>
public bool OptionKeepAlive { get; set; }
```

```csharp
/// <summary>
/// Option: TCP keep alive time
/// </summary>
/// <remarks>
/// The number of seconds a TCP connection will remain alive/idle before keepalive probes are sent to the remote
/// </remarks>
public int OptionTcpKeepAliveTime { get; set; } = -1;
```

```csharp
/// <summary>
/// Option: TCP keep alive interval
/// </summary>
/// <remarks>
/// The number of seconds a TCP connection will wait for a keepalive response before sending another keepalive probe
/// </remarks>
public int OptionTcpKeepAliveInterval { get; set; } = -1;
```

```csharp
/// <summary>
/// Option: TCP keep alive retry count
/// </summary>
/// <remarks>
/// The number of TCP keep alive probes that will be sent before the connection is terminated
/// </remarks>
public int OptionTcpKeepAliveRetryCount { get; set; } = -1;
```

```csharp
/// <summary>
/// Option: no delay
/// </summary>
/// <remarks>
/// This option will enable/disable Nagle's algorithm for TCP protocol
/// </remarks>
public bool OptionNoDelay { get; set; }
```

```csharp
/// <summary>
/// Option: reuse address
/// </summary>
/// <remarks>
/// This option will enable/disable SO_REUSEADDR if the OS support this feature
/// </remarks>
public bool OptionReuseAddress { get; set; }
```

```csharp
/// <summary>
/// Option: enables a socket to be bound for exclusive access
/// </summary>
/// <remarks>
/// This option will enable/disable SO_EXCLUSIVEADDRUSE if the OS support this feature
/// </remarks>
public bool OptionExclusiveAddressUse { get; set; }
```

```csharp
/// <summary>
/// Option: receive buffer size
/// </summary>
public int OptionReceiveBufferSize { get; set; } = 8192;
```

```csharp
/// <summary>
/// Option: send buffer size
/// </summary>
public int OptionSendBufferSize { get; set; } = 8192;
```

```csharp
/// <summary>
/// Is the server started?
/// </summary>
public bool IsStarted { get; private set; }
```

```csharp
/// <summary>
/// Is the server accepting new clients?
/// </summary>
public bool IsAccepting { get; private set; }
```


## Control

```csharp
void Start()
```

Starts the web server in a background task in a non blocking way. Be sure to hold the main program's thread else it will exit.

```csharp
void Stop()
```

Stops the web server.


## Router

The [`Router`](./router) property contains all routes handled by the web server.
You can list them with `Router.Routes`.

## Inline Func

For a rapid prototyping, `Func` can be set inline.

```csharp
/// <summary>
/// Add Func content for GET request
/// Available arguments :
///     - ISimpleWSession session
///     - HttpRequest request
///     - any query string name
/// </summary>
/// <param name="url"></param>
/// <param name="handler"></param>
public void MapGet(string url, Delegate handler)
```

```csharp
/// <summary>
/// Add Func content for POST request
/// Available arguments :
///     - ISimpleWSession session
///     - HttpRequest request
///     - any query string name
/// </summary>
/// <param name="url"></param>
/// <param name="handler"></param>
public void MapPost(string url, Delegate handler)
```


## Dynamic Content

### Manual

```csharp
/// <summary>
/// Add dynamic content for a controller type which inherit from Controller
/// </summary>
/// <param name="controllerType">controllerType</param>
/// <param name="path">path (default is "/")</param>
void AddDynamicContent(Type controllerType, string path = "/")
```

This method will integrate the class in the `Router` as a REST API under the `path` endpoint.


#### Example

You have `UserController`, `DepartmentController` and `WorkController` classes 
and you want them to be served by the web server under the `/api` endpoint.

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.AddDynamicContent(typeof(UserController), "/api");
server.AddDynamicContent(typeof(DepartmentController), "/api");
server.AddDynamicContent(typeof(WorkController), "/api");
```

::: tip NOTE
You need to add a line for each `Controller` you want to integrate. 
This is a repetitive task and you can miss a thing. You should prefer the automatic
`AddDynamicContent()` method to integrate all Controllers.
:::


### Automatic

```csharp
/// <summary>
/// Add dynamic content by registered all controllers which inherit from Controller
/// </summary>
/// <param name="path">path (default is "/")</param>
/// <param name="excludes">List of Controller to not auto load</param>
void AddDynamicContent(string path = "/", IEnumerable<Type> excludes = null);
```

At runtime, this method will find all classes based on `Controller` class and integrate them in the `Router` as a REST API under the `path` endpoint.


#### Example

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.AddDynamicContent("/api");
```

The parameter `excludes` can be useful if you want to exclude some Controller from being integrated.


#### Example

Suppose you have a `MaintenanceController` class which must only be used when you want to make the web server in a special maintenance mode.

```csharp
var server = new SimpleWServer(IPAddress.Any, 2015);
server.AddDynamicContent("/api", new Type[] { typeof(MaintenanceController) });
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
/// Json Serializer/Deserializer
/// </summary>
public IJsonEngine JsonEngine { get; set; } = new SystemTextJsonEngine(SystemTextJsonEngine.OptionsSimpleWBuilder());
}
```

This property defines the Json engine used in server and controllers to serialize, deserialize and populate objects.
The default engine is `System.Text.Json` initialized with recommended options.

To change the engine just provide an object which implement the `IJsonEngine` interface

```csharp
public interface IJsonEngine {

    /// <summary>
    /// Serialize an object instance into json string
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    string Serialize<T>(T value);

    /// <summary>
    /// Deserialize a json string into an T object instance
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="json"></param>
    /// <returns></returns>
    T Deserialize<T>(string json);

    /// <summary>
    /// Deserialize a string into an anonymous object instance
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="json"></param>
    /// <param name="model"></param>
    T DeserializeAnonymous<T>(string json, T model);

    /// <summary>
    /// Populate T object instance from json string
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="json"></param>
    /// <param name="target"></param>
    /// <param name="includeProperties"></param>
    /// <param name="excludeProperties"></param>
    void Populate<T>(string json, T target, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null);

}
```


## SSE

```csharp
/// <summary>
/// Send data conformed to Server Sent Event to filtered SSE Sessions
/// </summary>
/// <param name="evt">the event name</param>
/// <param name="data">the data</param>
/// <param name="filter">filter the SSESessions (default: null)</param>
void BroadcastSSESessions(string evt, string data, Expression<Func<ISimpleWSession, bool>> filter = null)
```

To sent reponse to all active Servent Sent Events session. A `filter` is available to selected desired session.
The `evt` and `data` parameters correspond to the format of SSE message.


## WebSockets

### Manual

```csharp
/// <summary>
/// Add WEBSOCKET controller content for a controller type which inherit from Controller
/// </summary>
/// <param name="controllerType">controllerType</param>
/// <param name="path">path (default is "/websocket")</param>
void AddWebSocketContent(Type controllerType, string path = "/websocket")
```

This method will integrate the class in the `Router` as a websocket under the `path` endpoint.


### Automatic

```csharp
/// <summary>
/// Add WEBSOCKET controller content by registered all controllers which inherit from Controller
/// </summary>
/// <param name="path">path (default is "/websocket")</param>
/// <param name="excepts">List of Controller to not auto load</param>
void AddWebSocketContent(string path = "/websocket", IEnumerable<Type> excepts = null)
```

At runtime, this method will find all classes based on `Controller` class and integrate them in the `Router` as a websocket under the `path` endpoint.


### MulticastText()

```csharp
/// <summary>
/// Send Message to all active websocket clients
/// </summary>
/// <param name="text"></param>
/// <returns></returns>
bool MulticastText(string text)
```

```csharp
/// <summary>
/// Send Message to all active websocket clients
/// </summary>
/// <param name="buffer"></param>
/// <returns></returns>
bool MulticastText(byte[] buffer)
```

The `MulticastText` send messge to all active websocket clients.


## CORS

```csharp
/// <summary>
/// Setup CORS
/// </summary>
/// <param name="origin">Access-Control-Allow-Origin</param>
/// <param name="headers">Access-Control-Allow-Headers</param>
/// <param name="methods">Access-Control-Allow-Methods</param>
/// <param name="credentials">Access-Control-Allow-Credentials</param>
void AddCORS(string origin="*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials="true")
```

Setup the Cross-Origin Resource Sharing policy and so, add 4 headers to every response.
`server.AddCORS()` method should be called before any `server.AddStaticContent()`.

## Static Content

```csharp
/// <summary>
/// Add static content
/// The timeout parameter control how long the content is cached (null or 0 mean no cache at all)
/// When cache, there is an underlying file watcher to refresh cache on file change
/// </summary>
/// <param name="path">Static content path</param>
/// <param name="prefix">Cache prefix (default is "/")</param>
/// <param name="filter">Cache filter (default is "*.*")</param>
/// <param name="timeout">Refresh cache timeout (0 or null mean no cache, default: null)</param>
void AddStaticContent(string path, string prefix = "/", string filter = "*.*", TimeSpan? timeout = null)
```

This method expose all files in `path` in the `Router` and served by the web server under the `prefix` endpoint.
There is a `filter` and a `timeout` to control the cache lifetime (default: 1 hour).


```csharp
/// <summary>
/// File to get by default (default: "index.html")
/// </summary>
string DefaultDocument { get; set; } = "index.html";
```

This property change the default file in of a static content of no file has been selected.

```csharp
/// <summary>
/// Enable AutoIndex when DefaultDocument does not exists
/// scope : global to all AddStaticContent()
/// </summary>
bool AutoIndex { get; set; } = false;
```

This property enable or disable the index feature which list files of a static 
content directory when no file has been selected and `DefaultDocument` does not exists.

## Telemetry

```csharp
/// <summary>
/// True to generate Telemetry Traces, Logs and Metrics
/// </summary>
public bool EnableTelemetry { get; set; } = false;
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


## Statistics

```csharp
/// <summary>
/// Number of sessions connected to the server
/// </summary>
public long ConnectedSessions { get { return Sessions.Count; } }
```

```csharp
/// <summary>
/// Number of bytes pending sent by the server
/// </summary>
public long BytesPending { get { return _bytesPending; } }
```

```csharp
/// <summary>
/// Number of bytes sent by the server
/// </summary>
public long BytesSent { get { return _bytesSent; } }
```

```csharp
/// <summary>
/// Number of bytes received by the server
/// </summary>
public long BytesReceived { get { return _bytesReceived; } }
```
