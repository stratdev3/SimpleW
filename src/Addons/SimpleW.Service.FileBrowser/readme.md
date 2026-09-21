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

server.ConfigureChallenge(session =>
    session.Response.Redirect("/auth/login").SendAsync());

server.UseFileBrowserModule(options => {
    options.Path = @"C:\uploads";
    options.Prefix = "/files";
    options.EventsPrefix = "/files/api/events";
    options.EnableEvents = true;
    // options.ClientPath = @"C:\custom-filebrowser-client";
    options.Authorize = (session, context) => session.Principal.IsAuthenticated
                                                ? AuthorizeResult.Allowed
                                                : AuthorizeResult.Challenge;
    options.ScopeKey = session => session.Principal.Identity.Identifier!;
    options.UploadChunkThresholdBytes = 100 * 1024 * 1024;
    options.UploadChunkBytes = 16 * 1024 * 1024;
    options.UploadSessionTimeout = TimeSpan.FromMinutes(30);
    options.MaxConcurrentUploadSessions = 100;
    options.OperationHistoryTimeout = TimeSpan.FromMinutes(5);
    options.DefaultPageSize = 100;
    options.MaxPageSize = 1000;
});

await server.RunAsync();
```

`Authorize` defaults to `(_, _) => AuthorizeResult.Allowed`, allowing unrestricted public access without additional configuration. Assign a custom callback to restrict access based on the session and the action/resource context. See the fine-grained example below for a restrictive policy.

Use `server.ConfigureChallenge(...)` when an `Authorize` callback returns `AuthorizeResult.Challenge` to redirect to a login URL, return `401` with `WWW-Authenticate`, or produce another application-owned authentication response. The callback is shared with the other server modules and must send the response. When omitted, the existing `403` response is preserved. Returning `Forbidden` denies module access or a concrete action with `403` without invoking the server challenge.

A browser navigation does not forward an `Authorization: Bearer` header from the page containing the link. A bearer-only application can use the server challenge to redirect to its own bootstrap endpoint, recover or request authentication there, establish a short-lived browser credential, and return to FileBrowser. The module does not put bearer tokens in URLs or prescribe a token transport.

`ScopeKey` must return the same non-empty value for every request from one owner. It isolates that owner's SSE room, upload sessions, and operations. The default uses the authenticated principal identifier, email, or name and falls back to `anonymous`; configure it explicitly when authentication does not populate `session.Principal` or when tenant-level isolation is required.

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

`GET /files/api/operations/:id` returns the state and retained result of an operation owned by the current scope. `POST /files/api/operations/:id/cancel` requests cancellation of that operation only. Terminal states remain queryable for `OperationHistoryTimeout`, so clients can follow a `202 Accepted` response even without SSE. `DELETE /files/api/uploads/:id` cancels an owned upload session.

Upload sessions expire after `UploadSessionTimeout` without activity, and at most `MaxConcurrentUploadSessions` sessions may be active at once. Old orphaned `.part` files are removed at startup and periodically. `GET /files/api/uploads/:id` returns each file's received byte ranges so a client can resume only the missing chunks. `DELETE /files/api/uploads/:id` cancels one session and removes its temporary files.

`UploadChunkBytes` must be lower than or equal to `SimpleWServerOptions.MaxRequestBodySize`, because SimpleW validates each request body before the module receives it.

## Authorization

### Fine-grained authorization

`Authorize` is a synchronous `Func<HttpSession, FileBrowserAuthorizationContext, AuthorizeResult>` that defaults to `(_, _) => AuthorizeResult.Allowed`, allowing all actions. Its action describes a business permission; normalized resources provide path-level control. Every access decision uses this callback, including anonymous access. Assign a custom callback to restrict access; explicitly assigning `null` is invalid.

Every endpoint request first checks `AccessModule` with an empty resource list. UI, configuration and SSE access use only that check. Other valid endpoint requests then check their business permission exactly once, using the complete resource context. Returning `AuthorizeResult.Challenge` from either check invokes the server challenge, or returns `403` if none is configured. `Forbidden` and unknown values return `403` without a challenge; only `Allowed` continues. Malformed requests may fail validation before a resource context can be built.

| Action | Permission |
| --- | --- |
| `AccessModule` | General access, browser UI, configuration and SSE connection. |
| `List` | List the requested directory and return its contents without per-entry authorization. |
| `ListTrash` | List the trash without per-entry authorization. |
| `Download` | Download a file, including HEAD, or calculate its checksum. |
| `Upload` | Create, inspect, receive, complete or cancel an upload. |
| `CreateFolder` | Create a directory and any missing parents. |
| `Rename` | Rename an entry and its descendants. |
| `Move` | Move an entry and its descendants. |
| `Delete` | Move entries to the trash. |
| `RestoreTrash` | Restore entries from the trash. |
| `PurgeTrash` | Permanently delete selected entries and their descendants. |
| `EmptyTrash` | Permanently delete all trash entries and their descendants. |
| `Archive` | Create a ZIP. |
| `Extract` | Extract a ZIP. |

`FileBrowserAuthorizationContext` exposes `Action`, an immutable `Resources` collection, and nullable `UploadId` and `OperationId`. Status and cancellation of an operation reuse its original business action and immutable resources: following or cancelling a rename therefore checks `Rename`. Owner isolation through `ScopeKey` is still required; foreign upload and operation ids remain `404`. The separate original-action property and the former technical enum values have been removed without aliases.

Each immutable resource exposes `Path`, optional `DestinationPath`, nullable `IsDirectory`, and optional `TrashId`/`TrashRelativePath`. Paths use `/`, are relative to `options.Path`, and use `""` for the root. `Path` is the source or, for creation, the target. `DestinationPath` pairs sources with destinations for move, rename, restore, archive and extract. Archive descendants share the output ZIP path; extraction outputs have their own target `Path`. No physical or temporary storage path is exposed.

For trash resources, `TrashRelativePath` is `""` at the payload root. `Path` is the original location when available; it is `null` for legacy entries without metadata, including their descendants. Authorize those entries explicitly by trash identity, or deny them. Restoration supplies the destination even when the original path is unknown.

For modifications, the complete resource context is prepared in read-only preflight, including descendants and implicitly created parent directories. The business action is authorized once before any filesystem modification or queue submission. A refusal rejects the whole action, including `EmptyTrash`. There is no intermediate callback for explicit targets and no callback per descendant. Malformed, incomplete or unavailable requests can fail preflight before the business callback is reached.

`List` receives only the requested directory. If authorized, its contents are returned with the existing search, sorting, pagination and security exclusions; child entries do not trigger authorization callbacks. `ListTrash` runs once with no resources and exposes the trash listing without permission filtering. Seeing an entry grants no download or modification permission: those actions are authorized independently when requested.

Each upload request checks `Upload` exactly once with the affected file resources and its session id. Session-wide actions include all declared files; file and chunk requests include the relevant file. Status and cancellation do not inspect future destinations on disk. The completion request authorizes all files and any missing parent directories once, before queue submission.

Queued work never retains `HttpSession`. A filesystem snapshot is verified internally before execution without calling `Authorize` again; a changed scope fails with `authorization_scope_changed`. ZIP creation reads only the approved manifest, and extraction checks the opened archive against approved outputs. Permissions are sampled per request, not continuously during accepted work or an open SSE connection. Filesystem checks do not provide an OS transaction against concurrent external changes or rollback for I/O errors. SSE rooms retain `ScopeKey` isolation; choose distinct scopes for users who must not share operation events.

Separate HTTP requests are authorized independently, including each upload chunk and each listing refresh. Client refresh behavior is unchanged.

The configuration endpoint does not return `capabilities`. The UI offers actions and displays server refusals; it does not query permissions per button.

For example, authenticated users may list the root and read `shared`, editors may modify `shared`, and administrators may also manage trash and legacy entries. Listing the root shows all of its entry names, including entries outside `shared`; accessing those entries still requires the corresponding action permission:

```csharp
options.Authorize = (session, context) => {
    if (context.Action == FileBrowserAction.AccessModule) {
        return session.Principal.IsAuthenticated
            ? AuthorizeResult.Allowed
            : AuthorizeResult.Challenge;
    }
    if (session.Principal.IsInRole("file-admin")) {
        return AuthorizeResult.Allowed;
    }

    // Operation status/cancellation already carry the original business permission.
    bool read = context.Action is FileBrowserAction.List or FileBrowserAction.Download;
    bool edit = context.Action is FileBrowserAction.Upload or FileBrowserAction.CreateFolder
        or FileBrowserAction.Rename or FileBrowserAction.Move or FileBrowserAction.Delete
        or FileBrowserAction.Archive or FileBrowserAction.Extract;
    if (!read && !(edit && session.Principal.IsInRole("file-editor"))) {
        return AuthorizeResult.Forbidden;
    }

    static bool InShared(string? path) => path != null
        && (path.Length == 0 || path.Equals("shared", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("shared/", StringComparison.OrdinalIgnoreCase));

    return context.Resources.All(resource => InShared(resource.Path)
        && (resource.DestinationPath == null || InShared(resource.DestinationPath)))
        ? AuthorizeResult.Allowed
        : AuthorizeResult.Forbidden;
};
```

## File checksums

Use **Checksum** on a file row or select one file and choose **Checksum** in the selection toolbar. The dialog calculates SHA-256 by default and also offers SHA-1 and MD5. **Copy** copies the uppercase hexadecimal checksum; the value remains selectable when clipboard access is unavailable. Calculation runs on demand, reads the file as a stream, and is not cached. Closing the dialog or changing the algorithm cancels the previous request.

`GET /files/api/checksum?path=documents/report.pdf&algorithm=sha256` returns `{ "ok": true, "path": "documents/report.pdf", "algorithm": "sha256", "checksum": "..." }`. Routes are relative to the configured prefix. Accepted algorithms are `sha256`, `sha1`, and `md5`; omitting `algorithm` selects `sha256`. The endpoint uses `AccessModule` followed by `Download` with the normalized file resource and returns `Cache-Control: no-store`.

Compare with the local file using the same algorithm, for example in PowerShell:

```powershell
Get-FileHash -LiteralPath 'C:\downloads\report.pdf' -Algorithm SHA256
```

Use `SHA1` or `MD5` when selected in the dialog. Matching checksums indicate matching file contents. This is a manual check of the current server file; uploads and downloads are not automatically verified.

Errors use `{ "ok": false, "error": "..." }`: unsupported algorithms return `400 invalid_algorithm`, missing files or directories return `404 file_not_found`, `Forbidden` access decisions return `403` (an explicit `Challenge` uses the configured authentication response), and other read failures return `500 checksum_failed`. If a size or modification-time change is detected during reading, the endpoint returns `409 file_changed`; retry once the file is stable. Invalid paths and reparse points are rejected using the existing path-validation errors.

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
