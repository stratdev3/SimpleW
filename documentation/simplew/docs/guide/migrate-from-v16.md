# Migration guide from SimpleW v16


## Core

- `SimpleWServer.Start()` => `SimpleWServer.StartAsync()` or `SimpleWServer.RunAsync()` depending on the blocking context you want
- `SimpleWServer.Stop()` => `SimpleWServer.StopAsync()`
- `SimpleWSServer` => [`SimpleWServer.UseHttps()`](./ssl-certificate.md) to setup ssl certificate
- `SimpleWServer.AddDynamicContent()` => `SimpleWServer.MapControllers()` and `SimpleWServer.MapController()`
- `SimpleWServer.AddStaticContent()` => `SimpleWServer.UseStaticFilesModule()`
- `SimpleWServer.AddCORS()` => `SimpleWServer.UseCorsModule()`
- `SimpleWServer.EnableTelemetry` => `SimpleWServer.EnableTelemetry()` and `SimpleWServer.DisableTelemetry()`


## Request

- `Request.Header()` => `Request.Headers.*` or `Request.Headers.TryGetValue("name", out string value)`


## Response

- `Response.MakeResponse()` depending on the context, build you response with `Response.Text()`, `Response.Body()`...
- `Response.MakeUnAuthorizedResponse()` => `Response.UnAuthorized()`
- `Response.MakeForbiddenResponse()` => `Response.Forbidden()`
- `Response.MakeInternalServerErrorResponse()` => `Response.InternalServerError()`
- `Response.MakeNotFoundResponse()` => `Response.NotFound()`
- `Response.MakeRedirectResponse()` => `Response.Redirect()`
- `Response.MakeAccessResponse()` => `Response.Access()`
- `Response.MakeDownloadResponse()` => depending on the context `Response.Text()` or `Response.Body()` and then `Response.Body().Attachment(outputFilename)`.


## Controller

- `webuser` => `User`


## Extensions

- `NetCoreServerExtension` => `SimpleWExtension`
- `NetCoreServerExtension.CreateJwt()` => `Session.CreateJwt()`


## Removed

- All the statistics properties from `SimpleWServer` class (`ConnectedSessions`, `BytesPending`, `BytesSent`, `BytesReceived`) has been removed. They may be appears is a future release but not now.
- `SimpleWServer.OptionSendBufferSize` property no longer exists;
- `SimpleWServer.TrustXHeaders` property no longer exists (see [`SimpleWServer.ConfigureTelemetry()`](./observability.md) on how to add custom telemetry tags);
