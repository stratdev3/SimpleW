# Changelog


## Unreleased

### feature

- Add opt-in recursive disk monitoring with shared SSE invalidation, bounded notification batching and watcher recovery.
- Refresh browser listings and trash automatically while preserving visible selections and dialog inputs; resynchronize after SSE reconnects.
- Initial `SimpleW.Service.FileBrowser` package release for SimpleW v26.1.
- Add a file browser and chunked upload module for SimpleW.
- Add server-side search, sorting, and cursor-based pagination to directory listings.
- Add searchable, sortable, paginated navigation with shareable URL state to the embedded web UI.
- Add secure file downloads from file-name links and refine the embedded file/search icons.
- Isolate SSE events, uploads, and retained operation state by configurable owner scope.
- Callbacks with action/resource authorization, recursive preflight and immutable queued scopes (breaking alpha API change).
- Default `Authorize` to `(_, _) => AuthorizeResult.Allowed` and remove the `AllowAnonymous` option (breaking alpha API change).
- Add per-operation status and cancellation endpoints with expiring terminal history.
- Use the shared `AuthorizeResult` enum for module and action authorization: `Allowed`, `Challenge`, or `Forbidden` (breaking alpha API change). Invoke the server-wide challenge only when explicitly requested.
