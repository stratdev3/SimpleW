# Migration guide from SimpleW v16

## Core

- `SimpleWSServer.Start()` has been replace by `SimpleWSServer.StartAsync()` or `SimpleWSServer.RunAsync()` depending on the blocking context you want.
- `SimpleWSServer.Stop()` has been replace by `SimpleWSServer.StopAsync()`
- `SimpleWSServer` class no longer exists. Use [`SimpleWServer.UseHttps()`](./ssl-certificate.md) to setup ssl certificate.
- `SimpleWSServer.AddDynamicContent()` has been replaced by `SimpleWSServer.UseControllers()` and `SimpleWSServer.UseController()`.
- `SimpleWSServer.AddStaticContent()` has been replaced by `SimpleWSServer.UseStaticFilesModule()`.
- `NetCoreServerExtension` class has been renamed `SimpleWExtension`.

## Removed

- All the statistics properties from `SimpleWServer` class (`ConnectedSessions`, `BytesPending`, `BytesSent`, `BytesReceived`) has been removed. They may be appears is a future release but not now.
- `SimpleWServer.OptionSendBufferSize` property no longer exists;