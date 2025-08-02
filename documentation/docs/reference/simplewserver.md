# Server

The `SimplewServer` is the main class to instanciate and manipulate the web server


## SimpleWServer()

```csharp
SimpleWServer(IPAddress address, int port)
```

The constructor takes an `IPAddress` and a `port` number.
You can use `IPAddress.Any` to match all IP addresses of the machine

```csharp
void Start()
```

Starts the web server in a background task in a non blocking way. Be sure to hold the main program's thread else it will exit.

```csharp
void Stop()
```

Stops the web server.


## SimpleWSServer()

```csharp
SimpleWSServer(SslContext context, IPAddress address, int port)
```

The class `SimpleWSServer` is exactly the same as `SimplewServer` except it takes a `SslContext` as primary argument.
This context is to use SSL Certificate.

```csharp
new SslContext(SslProtocols.Tls12, new X509Certificate2(@"certifcate.pfx", "password"));
```


## Router

The `Router` property contains all routes handled by the web server.
You can list them with `Router.Routes`.


## AddDynamicContent()

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


## SetToken()

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


## BroadcastSSESessions()

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


## AddWebSocketContent()

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


## MulticastText()

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


## AddCORS()

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


## AddStaticContent()

```csharp
/// <summary>
/// Add static content cache
/// </summary>
/// <param name="path">Static content path</param>
/// <param name="prefix">Cache prefix (default is "/")</param>
/// <param name="filter">Cache filter (default is "*.*")</param>
/// <param name="timeout">Refresh cache timeout (default is 1 hour)</param>
void AddStaticContent(string path, string prefix = "/", string filter = "*.*", TimeSpan? timeout = null)
```

This method expose all files in `path` in the `Router` and served by the web server under the `prefix` endpoint.
There is a `filter` and a `timeout` to control the cache lifetime (default: 1 hour).


## DefaultDocument

```csharp
/// <summary>
/// File to get by default (default: "index.html")
/// </summary>
string DefaultDocument { get; set; } = "index.html";
```

This property change the default file in of a static content of no file has been selected.


## AutoIndex

```csharp
/// <summary>
/// Enable AutoIndex when DefaultDocument does not exists
/// scope : global to all AddStaticContent()
/// </summary>
bool AutoIndex { get; set; } = false;
```

This property enable or disable the index feature which list files of a static 
content directory when no file has been selected and `DefaultDocument` does not exists.
