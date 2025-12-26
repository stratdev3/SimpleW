# Migration guide from SimpleW v16

## Core

- `SimpleWSServer.Start()` => `SimpleWSServer.StartAsync()` or `SimpleWSServer.RunAsync()` depending on the blocking context you want.
- `SimpleWSServer.Stop()` => `SimpleWSServer.StopAsync()`
- `SimpleWSServer` => [`SimpleWServer.UseHttps()`](./ssl-certificate.md) to setup ssl certificate.
- `SimpleWSServer.AddDynamicContent()` => `SimpleWSServer.MapControllers()` and `SimpleWSServer.MapController()`.
- `SimpleWSServer.AddStaticContent()` => `SimpleWSServer.UseStaticFilesModule()`.
- `NetCoreServerExtension` => `SimpleWExtension`.

## Removed

- All the statistics properties from `SimpleWServer` class (`ConnectedSessions`, `BytesPending`, `BytesSent`, `BytesReceived`) has been removed. They may be appears is a future release but not now.
- `SimpleWServer.OptionSendBufferSize` property no longer exists;