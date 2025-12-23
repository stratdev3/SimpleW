# IHttpModule

It is used by [`SimpleWServer.UseModule`](./simplewserver.md#usemodule) to install a module.


## Signature

A `IHttpModule` is an interface with the following method :

```csharp
/// <summary>
/// Callback to Install Module in server
/// </summary>
/// <param name="server"></param>
void Install(SimpleWServer server);
```

Example of implements : `StaticFilesModule` and `WebsocketModule`...
