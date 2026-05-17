# FileBrowser <Badge type="tip" text="Official" />

The [`SimpleW.Service.FileBrowser`](https://www.nuget.org/packages/SimpleW.Service.FileBrowser) package provides a web file browser and chunked upload module for SimpleW.

It exposes a ready-to-use browser UI and an HTTP API to list files and directories, create folders, rename or move entries, move entries to a trash directory, and upload complete directory trees.


## Features

- Browse files and directories below a configured root
- Create folders, rename entries, and move files or directories
- Move one or more entries to an internal trash directory
- Upload files and directory trees
- Split large files into configurable chunks
- Execute file mutations asynchronously through an in-process queue
- Publish operation and upload progress through Server-Sent Events
- Serve the UI from embedded DLL resources, a Debug source directory, or a custom directory
- Reject path traversal and hide internal trash and temporary upload directories


## Requirements

- .NET 8.0, 9.0, or 10.0
- SimpleW (core server)


## Installation

```sh
$ dotnet add package SimpleW.Service.FileBrowser
```

See the [changelog](./service-filebrowser-changelog.md)


## Minimal example

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
    options.Authorize = session => session.Principal.IsAuthenticated;
    options.UploadChunkThresholdBytes = 16 * 1024 * 1024;
    options.UploadChunkBytes = 8 * 1024 * 1024;
});

await server.RunAsync();
```

Open `http://localhost:8080/files/` after the authentication layer has populated `session.Principal`.

::: warning
The module is closed by default. Installation fails unless `Authorize` is configured or `AllowAnonymous` is explicitly set to `true`.
:::


## Configuration options

| Option | Default | Description |
|---|---:|---|
| `Path` | Required | Root directory exposed by the browser. It is created when the module is installed if it does not exist. |
| `Prefix` | `/files` | URL prefix shared by the UI and API. |
| `Authorize` | `null` | Callback invoked for UI, API, and SSE requests. Return `true` to allow access. |
| `AllowAnonymous` | `false` | Allows every request and takes precedence over `Authorize`. Use only for intentionally public or local instances. |
| `ServeUi` | `true` | Serves the bundled or disk-based web UI. The API remains available when disabled. |
| `ClientPath` | `null` | Explicit directory containing a custom UI. It overrides automatic Debug discovery and embedded resources. |
| `EnableEvents` | `true` | Enables the FileBrowser Server-Sent Events endpoint. |
| `EventsPrefix` | `Prefix + "/api/events"` | Custom SSE endpoint. With `Prefix = "/"`, the default is `/api/events`. |
| `MaxFileBytes` | `10 GiB` | Maximum declared size of one logical file. |
| `MaxUploadBytes` | `50 GiB` | Maximum combined size of all files in one upload session. Must be greater than or equal to `MaxFileBytes`. |
| `UploadChunkThresholdBytes` | `100 MiB` | Files larger than this value must use chunk upload. Set to `0` to chunk every non-empty file. |
| `UploadChunkBytes` | `16 MiB` | Maximum body size of one chunk request. |
| `TrashPath` | `Path/.trash` | Directory receiving deleted files and directories. It can be overridden with another directory. |

`Path`, `TrashPath`, and `ClientPath` are normalized to absolute directory paths during installation.


## Request body size

SimpleW validates each HTTP request body before FileBrowser receives it. Configure `SimpleWServerOptions.MaxRequestBodySize` so that it is at least as large as `UploadChunkBytes` and any file sent through the direct upload endpoint.

The following configuration keeps every individual request below 32 MiB:

```csharp
server.Configure(options => {
    options.MaxRequestBodySize = 32 * 1024 * 1024;
});

server.UseFileBrowserModule(options => {
    options.Path = "/srv/uploads";
    options.UploadChunkThresholdBytes = 16 * 1024 * 1024;
    options.UploadChunkBytes = 8 * 1024 * 1024;
    options.Authorize = session => session.Principal.IsAuthenticated;
});
```

`MaxFileBytes` and `MaxUploadBytes` limit logical uploads. They do not increase the server request body limit.


## UI delivery modes

The standard UI contains `index.html`, `app.js`, and `styles.css`. These files are embedded in `SimpleW.Service.FileBrowser.dll`, so the NuGet package does not copy a visible `client` directory into the consuming application.

The source is selected in this order:

1. `ClientPath`, when explicitly configured, in every build configuration
2. A complete local `SimpleW.Service.FileBrowser/client` source directory in Debug builds
3. The resources embedded in the DLL

Debug discovery only accepts a directory containing all three standard assets. Changes made to a selected disk directory are read directly without rebuilding the module. A Debug package used outside the repository falls back to the embedded resources when no complete source directory can be found.

Release builds use the embedded resources unless `ClientPath` is configured. Embedded assets support `GET`, `HEAD`, ETags, and `304 Not Modified`, and use `Cache-Control: no-cache`.

The module logs whether the UI is served from disk or from embedded resources.


## URL layout

With the default `Prefix = "/files"`:

| URL | Description |
|---|---|
| `/files` | Redirects to `/files/` while preserving the query string. |
| `/files/` | FileBrowser web UI. |
| `/files/app.js` | Bundled JavaScript client when embedded resources are selected. |
| `/files/styles.css` | Bundled stylesheet when embedded resources are selected. |
| `/files/api/*` | FileBrowser HTTP API. |
| `/files/api/events` | Default SSE endpoint. |

Set `Prefix = "/"` to host the UI at the application root and the API below `/api`. Set `ServeUi = false` to expose only the API and, when enabled, its SSE endpoint.


## Authorization

Use the same authorization callback for the UI, API, and event stream:

```csharp
server.UseFileBrowserModule(options => {
    options.Path = "/srv/private-files";
    options.Authorize = session =>
        session.Principal.IsAuthenticated
        && session.Principal.IsInRole("file-admin");
});
```

Unauthorized requests receive `403 Forbidden` with:

```json
{
  "ok": false,
  "error": "forbidden"
}
```

For a local development-only browser, anonymous access can be enabled explicitly:

```csharp
options.AllowAnonymous = true;
```

::: danger
Do not expose a writable filesystem root anonymously on an untrusted network.
:::


## File operations

The browser works with relative paths below `Path`. Mutating operations are placed in a single in-process queue and normally return `202 Accepted` with an operation id:

```json
{
  "ok": true,
  "operationId": "ad11e8da-2022-4b38-a647-7344119b9dd9",
  "operation": "rename",
  "path": "reports/draft.txt"
}
```

The queue performs create, rename, move, delete, and upload finalization operations sequentially. Queue and upload-session state are held in memory and do not survive a process restart.

Deleting an entry does not erase it immediately. The module moves it to `TrashPath` using a unique timestamped name. FileBrowser does not expose an API to empty or restore the trash; retention and cleanup of that directory remain application responsibilities.


## HTTP API

The bundled UI uses the following endpoints. They can also be used by a custom client.

| Method | Route | Request | Success |
|---|---|---|---:|
| `GET` | `/api/config` | None | `200` |
| `GET` | `/api/list?path=reports` | Relative directory path; omit `path` for the root | `200` |
| `POST` | `/api/folders` | `{ "path": "reports/2026" }` | `202` |
| `POST` | `/api/rename` | `{ "path": "reports/draft.txt", "name": "final.txt" }` | `202` |
| `POST` | `/api/move` | `{ "sourcePath": "draft.txt", "destinationDirectory": "reports", "name": "final.txt" }` | `202` |
| `POST` | `/api/delete` | `{ "path": "old.txt" }` or `{ "paths": ["a.txt", "b.txt"] }` | `202` |
| `POST` | `/api/operations/cancel` | No body required | `202` |
| `POST` | `/api/uploads` | `{ "files": [{ "path": "data.csv", "size": 1024 }] }` | `201` |
| `POST` | `/api/uploads/:id/files` | Binary body and `X-File-Path` header | `200` |
| `POST` | `/api/uploads/:id/chunks` | Binary body with `X-File-Path` and `X-Chunk-Offset` headers | `200` |
| `POST` | `/api/uploads/:id/complete` | No body required | `202` |

The routes in this table are relative to `Prefix`. With the default prefix, `/api/list` is available at `/files/api/list`.

`X-File-Path` can be replaced by a `path` query parameter on the two binary upload endpoints.

::: info
FileBrowser currently lists and manages filesystem entries but does not expose an endpoint for downloading file contents.
:::


## Upload protocol

A custom client uploads files in three stages.

### 1. Create the upload session

```http
POST /files/api/uploads
Content-Type: application/json

{
  "files": [
    { "path": "documents/readme.txt", "size": 1200 },
    { "path": "videos/demo.mp4", "size": 524288000 }
  ]
}
```

The response contains the upload id and the effective threshold and chunk size:

```json
{
  "ok": true,
  "uploadId": "20dffacb-d832-4eb7-aac9-a41a2fbc8ae9",
  "chunkThresholdBytes": 16777216,
  "chunkBytes": 8388608
}
```

The complete upload is rejected when one file exceeds `MaxFileBytes`, the combined size exceeds `MaxUploadBytes`, or a path is duplicated or invalid.


### 2. Send each file

Files at or below the chunk threshold can be sent in one request:

```http
POST /files/api/uploads/20dffacb-d832-4eb7-aac9-a41a2fbc8ae9/files
X-File-Path: documents/readme.txt
Content-Type: application/octet-stream

<binary body>
```

Larger files use one or more chunk requests:

```http
POST /files/api/uploads/20dffacb-d832-4eb7-aac9-a41a2fbc8ae9/chunks
X-File-Path: videos/demo.mp4
X-Chunk-Offset: 8388608
Content-Type: application/octet-stream

<binary chunk>
```

The offset is zero-based. A chunk must be non-empty, must not exceed `UploadChunkBytes`, and must remain within the declared file size.


### 3. Complete the upload

After every declared byte range has been received:

```http
POST /files/api/uploads/20dffacb-d832-4eb7-aac9-a41a2fbc8ae9/complete
```

Finalization creates missing parent directories and replaces an existing file with the uploaded file. It is queued like the other mutating operations and returns `202 Accepted`.


## Server-Sent Events

When `EnableEvents` is enabled, the UI opens an `EventSource` on `EventsPrefix`. The stream uses the same `Authorize` callback as the rest of the module.

| Event | Description |
|---|---|
| `filebrowser.connected` | Confirms that the SSE connection has joined the FileBrowser room. |
| `filebrowser.operation.started` | A queued mutation started. |
| `filebrowser.operation.completed` | A mutation completed successfully. |
| `filebrowser.operation.failed` | A mutation failed. |
| `filebrowser.operation.cancelled` | A queued or running mutation observed cancellation. |
| `filebrowser.upload.progress` | A direct file or chunk was received. |
| `filebrowser.upload.completed` | An uploaded file was moved to its final destination. |
| `filebrowser.upload.cancelled` | An active upload session was cancelled. |
| `filebrowser.changed` | A directory should be refreshed after a successful mutation. |

`POST /api/operations/cancel` requests cancellation for all pending or running FileBrowser operations and active upload sessions. Cancellation is best effort: a filesystem operation that has already completed cannot be rolled back.


## Path and internal directory safety

- API paths are relative to the configured root.
- `.` and `..` segments are rejected.
- Invalid filename characters and attempts to escape the root are rejected.
- The root itself cannot be renamed, moved, or deleted.
- `.trash` and `.filebrowser-tmp` are hidden from listings and cannot be addressed through the API.
- Moving a directory into itself is rejected.
- Existing destinations are rejected by create, rename, and move operations.

The module creates `Path`, `TrashPath`, and `Path/.filebrowser-tmp` during installation.


## Run the bundled example

The example application includes a local FileBrowser scenario:

```sh
example filebrowser --directory "C:\uploads" --browser
```

`--directory` selects the filesystem root and `--browser` opens `/files/`. The example enables anonymous access intentionally and prints a warning; use an authorization callback in an application exposed beyond local development.
