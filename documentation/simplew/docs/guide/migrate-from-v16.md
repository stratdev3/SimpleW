# Migration guide from SimpleW v16

## Notes

Consider the `v26 = v2` like the `v16 = v1`.

Even if some high level classes/methods has been renamed, your understanding of their role remains quite the same
and the bump version should not be a pain.

For example, I've migrated a full project _(thirty controllers / four hundred methods)_ on a couple of hours :
- 1h : 90% of searches and replaces _(mostly Request and Response syntax change)_
- 1h : 10% of real things to rewrite/adapt _(SimpleWServer options, CORS, WebSockets, Telemetry)_

If you have issue with the migration, [ping me on Discord](https://discord.gg/mDNRjyV8Ak) and I'll try to look at it.

You can stay in v16, but I will never update/fix that one. The v26 is the new better way to go, and a smart move for a long run.


## Code breaking change

How to replace old class/method/property with the new one.

### Core

- `SimpleWServer.Start()` => `SimpleWServer.StartAsync()` or `SimpleWServer.RunAsync()` depending on the blocking context you want
- `SimpleWServer.Stop()` => `SimpleWServer.StopAsync()`
- `SimpleWSServer` => [`SimpleWServer.UseHttps()`](./ssl-certificate.md) to setup ssl certificate
- `SimpleWServer.AddDynamicContent()` => `SimpleWServer.MapControllers()` and `SimpleWServer.MapController()`
- `SimpleWServer.AddStaticContent()` => [`SimpleWServer.UseStaticFilesModule()`](./staticfiles.md)
- `SimpleWServer.AddCORS()` => [`SimpleWServer.UseCorsModule()`](./cors.md)
- `SimpleWServer.EnableTelemetry` => `SimpleWServer.EnableTelemetry()` and `SimpleWServer.DisableTelemetry()`


### Request

- `Request.Header()` => `Request.Headers.*` or `Request.Headers.TryGetValue("name", out string value)`


### Response

- `Response.MakeResponse()` depending on the context, build you response with `Response.Text()`, `Response.Body()`...
- `Response.MakeUnAuthorizedResponse()` => `Response.UnAuthorized()`
- `Response.MakeForbiddenResponse()` => `Response.Forbidden()`
- `Response.MakeInternalServerErrorResponse()` => `Response.InternalServerError()`
- `Response.MakeNotFoundResponse()` => `Response.NotFound()`
- `Response.MakeRedirectResponse()` => `Response.Redirect()`
- `Response.MakeAccessResponse()` => `Response.Access()`
- `Response.MakeDownloadResponse()` => depending on the context `Response.Text()` or `Response.Body()` and then `Response.Body().Attachment(outputFilename)`.


### Controller

- `webuser` => `User`


### Extensions

- `NetCoreServerExtension` => `SimpleWExtension`
- `NetCoreServerExtension.CreateJwt()` => `Session.CreateJwt()`


### Removed

- All the statistics properties from `SimpleWServer` class (`ConnectedSessions`, `BytesPending`, `BytesSent`, `BytesReceived`) has been removed. They may be appears is a future release but not now.
- `SimpleWServer.OptionSendBufferSize` property no longer exists;
- `SimpleWServer.TrustXHeaders` property no longer exists (see [`SimpleWServer.ConfigureTelemetry()`](./observability.md) on how to add custom telemetry tags);
