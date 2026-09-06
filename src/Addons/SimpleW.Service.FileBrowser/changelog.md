# Changelog


## Unreleased

### feature

- Initial `SimpleW.Service.FileBrowser` package release for SimpleW v26.1.
- Add a file browser and chunked upload module for SimpleW.
- Add server-side search, sorting, and cursor-based pagination to directory listings.
- Add searchable, sortable, paginated navigation with shareable URL state to the embedded web UI.
- Add secure file downloads from file-name links and refine the embedded file/search icons.
- Isolate SSE events, uploads, and retained operation state by configurable owner scope.
- Add granular list, download, upload, modify, delete, trash, and path capabilities.
- Add per-operation status and cancellation endpoints with expiring terminal history.
- Integrate the server-wide asynchronous authentication challenge for module-wide authorization refusals.
