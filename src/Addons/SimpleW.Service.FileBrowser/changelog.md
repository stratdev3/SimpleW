# Changelog


## v26.1.1 - _(2026-09-26)_

### feature

- Embedded web UI for browsing files and directories, with search, sorting, cursor-based pagination and shareable navigation URLs.
- File downloads, folder creation, renaming and moving files and directories.
- Trash management with restoration, permanent deletion and emptying.
- File and directory uploads with chunked transfers, resumable sessions, progress reporting and automatic cleanup of expired sessions.
- ZIP archive creation and managed ZIP/RAR extraction, including multipart RAR archives, path validation and expanded-size limits.
- SHA-256, SHA-1 and MD5 file checksums.
- Module and action/resource authorization using `AuthorizeResult.Allowed`, `AuthorizeResult.Challenge` and `AuthorizeResult.Forbidden`, with server-wide authentication challenges.
- Recursive authorization checks and captured resource scopes for queued operations.
- Configurable owner scopes for SSE events, upload sessions and operation tracking.
- Background file operations with status and cancellation endpoints and expiring terminal history.
- Live SSE updates for operations and uploads, with automatic listing and trash refreshes that preserve selections and dialog inputs.
- Optional recursive filesystem monitoring with batched notifications, watcher recovery and resynchronization after SSE reconnects.
