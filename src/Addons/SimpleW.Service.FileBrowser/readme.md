# SimpleW.Service.FileBrowser

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![License](https://img.shields.io/badge/license-MIT-7737d1.svg)](https://github.com/Stratdev3/SimpleW/blob/master/licence)
[![NuGet](https://img.shields.io/nuget/v/SimpleW.Service.FileBrowser?color=7737d1&label=version&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.FileBrowser)
[![Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.FileBrowser?color=7737d1&label=downloads&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.FileBrowser)

`SimpleW.Service.FileBrowser` provides a small web file browser for SimpleW.

It can list files and directories, create folders, rename, move, manage an internal trash directory, upload files or directories, and create or safely extract ZIP archives. Large files are uploaded in chunks.

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
    options.UploadSessionTimeout = TimeSpan.FromMinutes(30);
    options.MaxConcurrentUploadSessions = 100;
    options.DefaultPageSize = 100;
    options.MaxPageSize = 1000;
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

Upload sessions expire after `UploadSessionTimeout` without activity, and at most `MaxConcurrentUploadSessions` sessions may be active at once. Old orphaned `.part` files are removed at startup and periodically. `GET /files/api/uploads/:id` returns each file's received byte ranges so a client can resume only the missing chunks. `DELETE /files/api/uploads/:id` cancels one session and removes its temporary files.

`UploadChunkBytes` must be lower than or equal to `SimpleWServerOptions.MaxRequestBodySize`, because SimpleW validates each request body before the module receives it.

## Listing, search, sorting, and pagination

`GET /files/api/list` lists only the direct children of the requested directory. Results are paginated by default and directories always appear before files.

| Query parameter | Description | Default |
| --- | --- | --- |
| `path` | Relative directory to list. | Root directory |
| `search` | Case-insensitive text contained in the item name. | Empty |
| `sort` | `name`, `size`, or `modified`. | `name` |
| `direction` | `asc` or `desc`. | `asc` |
| `pageSize` | Number of entries, up to `MaxPageSize`. | `DefaultPageSize` |
| `continuationToken` | Opaque token returned by the preceding page. | Empty |

```text
GET /files/api/list?path=documents&search=report&sort=modified&direction=desc&pageSize=100
```

The response contains `keyCount`, `isTruncated`, and `nextContinuationToken` in addition to `path`, `parent`, the effective search/sort parameters, and `items`. When `isTruncated` is true, pass `nextContinuationToken` unchanged as `continuationToken` to request the next page. Tokens are bound to the path, search, sort, direction, and page size that created them.

`GET /files/api/download?path=documents/report.pdf` streams an authorized file as an attachment. In the embedded UI, clicking a file name uses this endpoint to download it.

`GET /files/api/trash` lists deleted entries. `POST /files/api/trash/restore` restores selected trash ids to their original paths, or accepts `destinationPath` for one entry whose original path is unavailable. `POST /files/api/trash/delete` permanently removes selected ids, and `POST /files/api/trash/empty` permanently removes every trash entry. New deletions retain restore metadata across process restarts; the bundled UI asks for a destination when restoring a legacy entry.

`POST /files/api/archive` queues creation of a ZIP from one or more relative file or directory paths. The archive is built in the module's internal temporary directory and moved to its final destination only after successful completion.

`POST /files/api/extract` queues ZIP extraction with a relative archive path, a destination directory, and a `createDestinationDirectory` flag. Extraction rejects paths outside the destination, existing file conflicts, oversized entries, oversized expanded archives, and archives containing more than `MaxArchiveEntries` entries.
