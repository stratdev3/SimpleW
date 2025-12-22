# WebsocketModule

The `WebsocketModule` 


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
