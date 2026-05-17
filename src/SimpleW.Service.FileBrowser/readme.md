# SimpleW.Service.FileBrowser

`SimpleW.Service.FileBrowser` provides a small web file browser for SimpleW.

It can list files and directories, create folders, rename, move, delete to an internal trash directory, and upload files or directories. Large files are uploaded in chunks.

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.FileBrowser;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.Configure(options => {
    options.MaxRequestBodySize = 32 * 1024 * 1024;
});

server.UseFileBrowserModule(options => {
    options.Path = @"C:\uploads";
    options.Prefix = "/files";
    options.EventsPrefix = "/files/api/events";
    options.EnableEvents = true;
    // options.ClientPath = @"C:\custom-filebrowser-client";
    options.AllowAnonymous = true;
    options.UploadChunkThresholdBytes = 100 * 1024 * 1024;
    options.UploadChunkBytes = 16 * 1024 * 1024;
});

await server.RunAsync();
```

By default, the module is closed. Configure `Authorize` or set `AllowAnonymous = true`.

The web UI is embedded directly in `SimpleW.Service.FileBrowser.dll`; the NuGet package does not copy a `client/` directory to the consuming application.
Debug builds automatically serve the local `SimpleW.Service.FileBrowser/client` directory through `StaticFilesModule` when it can be found, so client changes are read directly from disk without rebuilding the module.
Release builds and Debug builds without a local client directory serve the embedded resources.
Set `ClientPath` to explicitly serve a custom client directory in any build configuration.

When events are enabled, the web UI opens an `EventSource` on `EventsPrefix`.
File operations return `202 Accepted` and publish:

- `filebrowser.operation.started`
- `filebrowser.operation.completed`
- `filebrowser.operation.failed`
- `filebrowser.operation.cancelled`
- `filebrowser.upload.progress`
- `filebrowser.upload.completed`
- `filebrowser.upload.cancelled`
- `filebrowser.changed`

`POST /files/api/operations/cancel` cancels pending/running operations and active upload sessions on a best-effort basis.

`UploadChunkBytes` must be lower than or equal to `SimpleWServerOptions.MaxRequestBodySize`, because SimpleW validates each request body before the module receives it.
