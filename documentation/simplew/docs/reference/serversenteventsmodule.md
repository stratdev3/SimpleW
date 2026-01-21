# ServerSentEventsModule

The `ServerSentEventsModule` is used to setup Server Sent Events.
This module setup a middleware.


## Definition

```csharp
/// <summary>
/// Use Server Sent Events Module
/// It setups a Middleware
/// </summary>
public static SimpleWServer UseServerSentEventsModule(this SimpleWServer server, Action<ServerSentEventsOptions>? configure = null)
```


## Example

See [examples](../guide/serversentevents.md.md) of use.