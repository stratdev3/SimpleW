# Migration guide from SimpleW v16

## Core

- `SimpleWServer.Start()` => `SimpleWServer.StartAsync()` or `SimpleWServer.RunAsync()` depending on the blocking context you want.
- `SimpleWServer.Stop()` => `SimpleWServer.StopAsync()`
- `SimpleWSServer` => [`SimpleWServer.UseHttps()`](./ssl-certificate.md) to setup ssl certificate.
- `SimpleWServer.AddDynamicContent()` => `SimpleWServer.MapControllers()` and `SimpleWServer.MapController()`.
- `SimpleWServer.AddStaticContent()` => `SimpleWServer.UseStaticFilesModule()`.
- `NetCoreServerExtension` => `SimpleWExtension`.
- `SimpleWServer.AddCORS()` => `SimpleWServer.UseCorsModule()`

## Removed

- All the statistics properties from `SimpleWServer` class (`ConnectedSessions`, `BytesPending`, `BytesSent`, `BytesReceived`) has been removed. They may be appears is a future release but not now.
- `SimpleWServer.OptionSendBufferSize` property no longer exists;