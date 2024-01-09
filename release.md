# RELEASE


Notes :
- it does not strictly follow the semver convention.
- when something break, it's written.
- no long time support or backporting for major version, just use the last update.
- if you find bug/optimization, feel free to send patch.

## vNEXT / _(2024-01-??)_
Maintenance

### breakingChange

- rename `SimpleW.SetTokenWebUserCallback()` method to `SimpleW.SetToken()` and reorder parameters (#3).

### feature

- comment



## v11.4.0 / _(2024-01-04)_
Maintenance

### feature

- property `RegExpEnabled` must be explicitly enabled to allow regular expression in route path (#2).
- refactor(Router): use `Dictionnary<Key, Route>` when `RegExpEnabled` is false, performances improvement (#1)



## v11.3.3 / _(2023-12-24)_
Maintenance

### feature

- update nuget package settings


## v11.3.2 / _(2023-12-23)_
Maintenance

### feature

- update nuget package settings



## v11.3.1 / _(2023-12-23)_
Maintenance

### bug

- fix wrong label and status for `OnWsDisconnected` logging



## v11.3.1 / _(2023-12-23)_
Maintenance

### bug

- fix wrong label and status for `OnWsDisconnected` logging

### feature

- add release-notes
- enhance logging



## v11.3.0 / _(2023-12-15)_
Maintenance

### breakingChange

- remove `Prefix` property from `RouteAttribute` class

### feature

- add new `AutoIndex` property to list documents when defaultDocument does not exists
- add new property `RegExpEnabled` to force Router handle regular expression in Routes



## v11.2.0 / _(2023-12-09)_
Maintenance

### feature

- sync with upstream (NetCoreServer fix websocket closing)
- split `Route` with a new `Router` class
- rename `ObjectMethodExecutor` to `ControllerMethodExecutor`
- use `StringComparison.OrdinalIgnoreCase`



## v11.1.0 / _(2023-11-07)_
Maintenance

### feature

- clean code
- add documentation
- add license
- include LitJWT sources in projet files



## v11.0.0 / _(2023-11-01)_
New Major Version

### bug

- fix huge performances degradation due to old `Trace.Trace*()` methods



## v10.0.0 / _(2023-10-22)_
New Major Version

### breakingChange

- rename `AddApiContent()` to `AddDynamicContent()`



## v9.0.1 / _(2023-10-22)_
Maintenance

### bug

- throw error on starting static server when containing file which size is 0

### feature

- reorganize `CompileHandler()` method
- refactor `ObjectMethodExecutor.Controllers()`
- add some check to `AddApiContent()` and `AddWebSocketContent()` methods
- refactor `RouteAttribute.SetPrefix()`
- clean code



## v9.0.0 / _(2023-10-12)_
New Major Version

### feature

- refactor `mimeTable`
- add NetCoreServer Official Readme
- sync with upstream (HttpMultipartDataParser to v8.2.0)



## v8.1.2 / _(2023-10-12)_
Maintenance

### feature

- compliant to OpenTelemetry http semantic convention



## v8.1.1 / _(2023-10-12)_
Maintenance

### feature

- convert old trace to `system.diagnostics.Activity` for OpenTelemetry



## v8.1.0 / _(2023-10-12)_
Maintenance

### feature

- preliminary support of `system.diagnostics.Activity` for OpenTelemetry



## v8.0.8 / _(2023-08-17)_
Maintenance

### feature

- change scope from protected to public for `Controller.MakeResponse*()`



## v8.0.7 / _(2023-08-16)_
Maintenance

### feature

- add new `MakeDownloadResponse(MemoryStream content)`



## v8.0.6 / _(2023-05-15)_
Maintenance

### breakingChange

- change `MakeRedirectResponse()` scope to private



## v8.0.5 / _(2023-05-15)_
Maintenance

### feature

- add new helper `Controller.MakeRedirectResponse()`



## v8.0.4 / _(2023-03-30)_
Maintenance

### feature

- clean code



## v8.0.3 / _(2023-03-29)_
Maintenance

### feature

- support `DateOnly` in `Controller.Method()` parameter



## v8.0.2 / _(2023-03-19)_
Maintenance

### feature

- sync with upstream (Newtonsoft v13.0.3)
- `BodyMap/JsonMap/BodyMapAnonymous` : add new json custom converter for hh:mm timeonly



## v8.0.1 / _(2023-02-16)_
Maintenance

### feature

- add new `Controller.MakeResponse()`



## v7.0.0 / _(2023-01-25)_
New Major Version

### breakingChange

- refactor SimpleW with `BodyMap()`, `BodyMapAnonymous()` and `BodyFile()`

### feature

- comment



## v7.0.0 / _(2022-12-01)_
New Major Version

### feature

- sync with upstream (NetCoreServer v7.0.0)
- sync with upstream (LitJWT v2.0.2)
- sync with upstream (Newtonsoft v13.0.2)
- retargeting to NET7



## v6.2.4 / _(2022-11-30)_
Maintenance

### feature

- uniformize http and websocket log



## v6.2.3 / _(2022-11-30)_
Maintenance

### feature

- comment
- replace special char `"` in logging url



## v6.2.1 / _(2022-10-20)_
Maintenance

### bug

- logging error and exception



## v6.2.0 / _(2022-10-05)_
Maintenance

### feature

- refactor `Controller.GetWebUser()` into `SetWebUser()` and `JwtToWebUser()`
- support `JWT` in `WebSocket` connexion



## v6.1.0 / _(2022-10-04)_
Maintenance

### feature

- refactor `SimpleWServer.AddWebSocketContent()`
- support `JWT` in `WebSocket`



## v6.0.0 / _(2022-10-02)_
New Major Version

### bug

- fix null exception in `Controller.GetJwt()` when parsing request url

### feature

- support `Websocket`
- support `Route` for websocket
- refactor `SimpleWSession/Controller` with `(SimpleWServer)Session.Server`


## v5.0.0 / _(2022-09-30)_
New Major Version

### breakingChange

- rename `AddRESTContent` to `AddApiContent`

### feature

- remove exception message output when http 500 error



## v4.4.5 / _(2022-09-28)_
Maintenance

### feature

- sync with upstream (NetCoreServer v6.6.0)
- sync with upstream (Http-Multipart-Data-Parser v7.0.0)



## v4.4.4 / _(2022-09-27)_
Maintenance

### feature

- support CORS
- add new `HttpResponse.MakeCORSResponse()` method



## v4.4.3 / _(2022-08-29)_
Maintenance

### bug

- replace `X-Scheme` header by `X-Forwarded-Proto`

### feature

- handle `X-Forwarded-Host` header in log



## v4.4.2 / _(2022-08-24)_
Maintenance

### feature

- support `X-Scheme` request header for logging



## v4.4.1 / _(2022-08-24)_
Maintenance

### feature

- log full request url for file in cache and normalize log url
- log real remote ip
- refactor `Route` with the new `FQURL()` method



## v4.4.0 / _(2022-08-24)_
Maintenance

### bug

- `shouldSerializeContractResolver()` handle nested class



## v4.3.2 / _(2022-08-12)_
Maintenance

### bug

- sync with upstream (NetCoreServer v6.5.0 fix websocket regression)



## v4.3.1 / _(2022-08-12)_
Maintenance

### feature

- add new parameter `description` to `RouteAttribute`



## v4.3.0 / _(2022-08-11)_
Maintenance

### feature

- sync with upstream (NetCoreServer v6.4.0 with Span/Memory)



## v4.2.8 / _(2022-07-29)_
Maintenance

### bug

- sync with upstream NetCoreServer (fix deadlock in websocker/wssession)
- sync with upstream NetCoreServer (Add HTTP cookie SameSite=Strict mode)

### feature

- comment
- add new parameter `excepts` to `SimpleW.AddRESTContent()` to exclude controllers
- support wildcard in `RouteAttribute`



## v4.2.7 / _(2022-06-28)_
Maintenance

### feature

- sync with upstream package LitJWT from v1.2.0 to v2.0.1



## v4.2.6 / _(2022-06-28)_
Maintenance

### breakingChange

### feature

- enhance http logging (time, status, user_agent...)



## v4.2.5 / _(2022-06-14)_
Maintenance

### feature

- add new parameter `expiration` to `NetCoreServerExtension.CreateJwt()` to control `JWT` expiration



## v4.2.4 / _(2022-04-27)_
Maintenance

### feature

- enhance http logging (time, status, user_agent...)



## v4.2.3 / _(2022-04-27)_
Maintenance

### feature

- add new paremeter `refresh` in `CreateJwt()` to control how `Session` should refresh the underlying 'IWebUser'



## v4.2.2 / _(2022-04-07)_
Maintenance

### breakingChange

- rename `NetCoreServerExtension.PostMap()` to `NetCoreServerExtension.JsonMap()`



## v4.2.1 / _(2022-04-07)_
Maintenance

### breakingChange

- rename `NetCoreServerExtension.GetBearerToken()` to `NetCoreServerExtension.GetBearer()`



## v4.2.0 / _(2022-04-04)_
Maintenance

### feature

- replace logging `console.log` by `system.diagnostics`



## v4.1.0 / _(2022-03-22)_
Maintenance

### breakingChange

- change some method scope of Route class to private
- rename `Route.Parse()` to `Route.ParseQueryString()`

### feature

- `JWT` HTTP Header can be override by a `jwt` querystring in url
- comment
- add new `NetCoreServerExtension.CreateJwt(IWebUser webuser)`
- refactor `ValidateJwt` with Generic
- refactor and simplify `TokenWebUser` class



## v4.0 / _(2022-03-21)_
New Major Version

### breakingChange

- rename class `Controller` to `ControllerBase`

### feature

- add new parameter `TokenExpiration` to `TokenWebUser`



## v3.2.2 / _(2022-03-20)_
Maintenance

### breakingChange

- rename `DelegateSetTokenWebUser(string login=null)` to `DelegateSetTokenWebUser(Guid id = new Guid())`



## v3.1.2 / _(2022-03-10)_
Maintenance

### bug

- ignore case when parsing http header authorization



## v3.1.1 / _(2022-03-08)_
Maintenance

### feature

- add new ControllerBase `MakeForbiddenResponse()` and `MakeAccessResponse()`



## v3.1.0 / _(2022-02-22)_
Maintenance

### feature

- replace `InvariantCulture` with `Ordinal`
- remove `JsonSerializerSettingsDefaultCulture` from `NetCoreServerExtension.PostMap()`
- enhance exception message in `NetCoreServerExtension.PostMap()`
- add comment



## v3.0.0 / _(2022-02-21)_
Maintenance

### feature

- retargeting to NET6



## v2.3.0 / _(2022-02-11)_
Maintenance

### feature

- add `Http-Multipart-Data-Parser`



## v2.2.1 / _(2022-02-03)_
Maintenance

### bug

- remove debug message



## v2.2 / _(2022-02-03)_
Maintenance

### bug

- fix NetCoreServer `FileCache.InsertFileInternal()` `ReadAllBytes()` error



## v2.1.1 / _(2022-01-31)_
Maintenance

### bug

- fix correct timestamp in console log



## v2.1 / _(2022-01-30)_
Maintenance

### bug

- fix netcoresever `FileCache`
- fix compressTypes in `ControllerBase.SendResponseAsync()`



## v2.0 / _(2022-01-23)_
New Major Version

### feature

- sync with upstream (NetCoreServer v6.1.0)



## v1.1.1 / _(2022-01-03)_
Maintenance

### refactor

- refactor `SimpleWServer.AddWebSocketContent()`



## v1.1.0 / _(2021-12-26)_
Initial nuget package release

### breakingChange

- rename `PostUpdate()` to `PostMap()`



## v1.0.15 / _(2021-12-16)_
Maintenance

### feature

- support Guid parameter in `Controller.Method()`



## v1.0.14 / _(2021-12-16)_
Maintenance

### bug

- parameter null converter in `Controller.Method()`



## v1.0.12 / _(2021-11-10)_
Initial stable release

### features

- serve Statics Files
- serve REST API
- routes
- set default document
- set custom mime type
- log with console.writeline