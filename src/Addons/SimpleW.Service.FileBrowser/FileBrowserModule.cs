using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using SimpleW.Modules;
using SimpleW.Observability;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Installs and operates the file browser HTTP API, UI, uploads and background file operations.
    /// </summary>
    internal sealed class FileBrowserModule : IHttpModule {

        #region constants and fields

        private const string TempDirectoryName = ".filebrowser-tmp";
        private const string TrashMetadataFileName = ".trash-item.json";
        private const string TrashPayloadName = "payload";
        private const string EventsRoom = "filebrowser";
        private static readonly ILogger _log = new Logger<FileBrowserModule>();
        private static readonly Lazy<ClientAsset[]> EmbeddedClientAssets = new(LoadEmbeddedClientAssets);
        private static readonly JsonSerializerOptions JsonOptions = new() {
            PropertyNameCaseInsensitive = true
        };
        private static readonly char[] InvalidSegmentChars = System.IO.Path.GetInvalidFileNameChars();

        private readonly FileBrowserOptions _options;
        private readonly ConcurrentDictionary<Guid, UploadSession> _uploads = new();
        private readonly object _uploadsSync = new();
        private readonly ConcurrentDictionary<Guid, QueuedOperation> _trackedOperations = new();
        private readonly Channel<QueuedOperation> _operations = Channel.CreateUnbounded<QueuedOperation>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false
        });
        private readonly string _tempPath;
        private readonly StringComparison _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private ServerSentEventsHub? _eventsHub;
        private CancellationTokenSource? _operationsCts;
        private Task? _operationsTask;
        private Task? _uploadCleanupTask;

        #endregion constants and fields

        #region initialization

        /// <summary>
        /// Creates a module from validated file browser options.
        /// </summary>
        /// <param name="options"></param>
        public FileBrowserModule(FileBrowserOptions options) {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
            _tempPath = System.IO.Path.Combine(_options.NormalizedPath, TempDirectoryName);
        }

        /// <summary>
        /// Creates required directories and registers the UI, API, events and lifecycle callbacks.
        /// </summary>
        /// <param name="server"></param>
        public void Install(SimpleWServer server) {

            EnsureNoReparsePoints(_options.NormalizedPath);
            Directory.CreateDirectory(_options.NormalizedPath);
            EnsureNoReparsePoints(_options.NormalizedPath);
            EnsureNoTrashReparsePoints(_options.NormalizedTrashPath);
            Directory.CreateDirectory(_options.NormalizedTrashPath);
            EnsureNoTrashReparsePoints(_options.NormalizedTrashPath);
            EnsureNoReparsePoints(_tempPath);
            Directory.CreateDirectory(_tempPath);
            EnsureNoReparsePoints(_tempPath);
            CleanupAbandonedUploadFiles(DateTimeOffset.UtcNow);

            if (_options.EnableEvents) {
                server.UseServerSentEventsModule(sse => {
                    sse.Prefix = _options.NormalizedEventsPrefix;
                    sse.AutoJoinRoom = EventsRoom;
                    sse.Authorize = IsAuthorized;
                    sse.OnConnect = async (connection, _) => {
                        await connection.SendEventAsync(
                            SerializeEvent(new {
                                ok = true,
                                eventPrefix = _options.NormalizedEventsPrefix,
                                connectedAtUtc = DateTimeOffset.UtcNow
                            }),
                            @event: "filebrowser.connected"
                        ).ConfigureAwait(false);
                    };
                    _eventsHub = sse.Hub;
                });
            }

            server.OnStateChanged(async (_, state) => {
                if (state == SimpleWServerState.Started) {
                    StartOperations();
                }
                else if (state == SimpleWServerState.Stopped) {
                    await StopOperationsAsync().ConfigureAwait(false);
                }
            });

            if (_options.ServeUi) {
                if (_options.NormalizedPrefix != "/") {
                    server.MapGet(_options.NormalizedPrefix, (HttpSession session) => RedirectUiRootAsync(session));
                    server.Map("HEAD", _options.NormalizedPrefix, (HttpSession session) => RedirectUiRootAsync(session));
                }

                if (TryResolveClientPath(out string clientPath)) {
                    server.UseStaticFilesModule(staticFiles => {
                        staticFiles.Path = clientPath;
                        staticFiles.Prefix = _options.NormalizedPrefix;
                        staticFiles.DefaultDocument = "index.html";
                        staticFiles.Authorize = IsAuthorized;
                    });
                    _log.Info($"serving UI from disk '{clientPath}'");
                }
                else {
                    InstallEmbeddedClient(server);
                    _log.Info("serving UI from embedded resources");
                }
            }

            server.MapGet(Route("/api/config"), (HttpSession session) => ConfigAsync(session));
            server.MapGet(Route("/api/list"), (HttpSession session) => ListAsync(session));
            server.MapGet(Route("/api/download"), (HttpSession session) => DownloadAsync(session));
            server.Map("HEAD", Route("/api/download"), (HttpSession session) => DownloadAsync(session));
            server.Map("POST", Route("/api/folders"), (HttpSession session) => CreateFolderAsync(session));
            server.Map("POST", Route("/api/rename"), (HttpSession session) => RenameAsync(session));
            server.Map("POST", Route("/api/move"), (HttpSession session) => MoveAsync(session));
            server.Map("POST", Route("/api/delete"), (HttpSession session) => DeleteAsync(session));
            server.MapGet(Route("/api/trash"), (HttpSession session) => ListTrashAsync(session));
            server.Map("POST", Route("/api/trash/restore"), (HttpSession session) => RestoreTrashAsync(session));
            server.Map("POST", Route("/api/trash/delete"), (HttpSession session) => DeleteTrashAsync(session));
            server.Map("POST", Route("/api/trash/empty"), (HttpSession session) => EmptyTrashAsync(session));
            server.Map("POST", Route("/api/archive"), (HttpSession session) => ArchiveAsync(session));
            server.Map("POST", Route("/api/extract"), (HttpSession session) => ExtractAsync(session));
            server.Map("POST", Route("/api/operations/cancel"), (HttpSession session) => CancelOperationsAsync(session));
            server.Map("POST", Route("/api/uploads"), (HttpSession session) => CreateUploadAsync(session));
            server.MapGet(Route("/api/uploads/:id"), (HttpSession session) => GetUploadAsync(session));
            server.Map("DELETE", Route("/api/uploads/:id"), (HttpSession session) => DeleteUploadAsync(session));
            server.Map("POST", Route("/api/uploads/:id/files"), (HttpSession session) => UploadFileAsync(session));
            server.Map("POST", Route("/api/uploads/:id/chunks"), (HttpSession session) => UploadChunkAsync(session));
            server.Map("POST", Route("/api/uploads/:id/complete"), (HttpSession session) => CompleteUploadAsync(session));

            _log.Info($"installed with prefix {_options.NormalizedPrefix}");
        }

        #endregion initialization

        #region user interface

        /// <summary>
        /// Combines a module-relative route with the configured URL prefix.
        /// </summary>
        /// <param name="suffix"></param>
        private string Route(string suffix) {
            if (_options.NormalizedPrefix == "/") {
                return suffix.StartsWith("/", StringComparison.Ordinal) ? suffix : "/" + suffix;
            }
            return _options.NormalizedPrefix + (suffix.StartsWith("/", StringComparison.Ordinal) ? suffix : "/" + suffix);
        }

        /// <summary>
        /// Redirects the prefix without a trailing slash to the UI root.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask RedirectUiRootAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            string location = _options.NormalizedPrefix + "/";
            if (!string.IsNullOrEmpty(session.Request.QueryString)) {
                location += "?" + session.Request.QueryString;
            }

            return session.Response.Redirect(location).SendAsync();
        }

        /// <summary>
        /// Resolves an explicit or development client directory when one is available.
        /// </summary>
        /// <param name="clientPath"></param>
        private bool TryResolveClientPath(out string clientPath) {
            if (!string.IsNullOrWhiteSpace(_options.NormalizedClientPath)) {
                clientPath = _options.NormalizedClientPath!;
                return true;
            }

#if DEBUG
            string baseDirectory = AppContext.BaseDirectory;
            string[] candidates = [
                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, "..", "..", "..", "..", "SimpleW.Service.FileBrowser", "client")),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, "..", "..", "..", "client")),
                System.IO.Path.Combine(baseDirectory, "SimpleW.Service.FileBrowser", "client"),
                System.IO.Path.Combine(baseDirectory, "client")
            ];

            foreach (string candidate in candidates) {
                if (IsClientDirectory(candidate)) {
                    clientPath = NormalizeDirectory(candidate);
                    return true;
                }
            }
#endif

            clientPath = string.Empty;
            return false;
        }

        /// <summary>
        /// Checks whether a directory contains the complete browser client.
        /// </summary>
        /// <param name="path"></param>
        private static bool IsClientDirectory(string path) {
            return Directory.Exists(path)
                && File.Exists(System.IO.Path.Combine(path, "index.html"))
                && File.Exists(System.IO.Path.Combine(path, "app.js"))
                && File.Exists(System.IO.Path.Combine(path, "styles.css"));
        }

        /// <summary>
        /// Returns an absolute directory path ending with a separator.
        /// </summary>
        /// <param name="path"></param>
        private static string NormalizeDirectory(string path) {
            string full = System.IO.Path.GetFullPath(path);
            if (!full.EndsWith(System.IO.Path.DirectorySeparatorChar) && !full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)) {
                full += System.IO.Path.DirectorySeparatorChar;
            }
            return full;
        }

        /// <summary>
        /// Registers routes for client assets embedded in the assembly.
        /// </summary>
        /// <param name="server"></param>
        private void InstallEmbeddedClient(SimpleWServer server) {
            foreach (ClientAsset asset in EmbeddedClientAssets.Value) {
                string route = Route(asset.RouteSuffix);
                server.MapGet(route, (HttpSession session) => EmbeddedClientAssetAsync(session, asset));
                server.Map("HEAD", route, (HttpSession session) => EmbeddedClientAssetAsync(session, asset));
            }
        }

        /// <summary>
        /// Sends an embedded client asset with ETag-based cache validation.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="asset"></param>
        private ValueTask EmbeddedClientAssetAsync(HttpSession session, ClientAsset asset) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            session.Response
                   .AddHeader("ETag", asset.ETag)
                   .AddHeader("Cache-Control", "no-cache");

            if (IfNoneMatchHasMatch(session.Request, asset.ETag)) {
                return session.Response.Status(304).SendAsync();
            }

            return session.Response.Body(asset.Data, asset.ContentType).SendAsync();
        }

        /// <summary>
        /// Loads the complete embedded browser client once for the process.
        /// </summary>
        private static ClientAsset[] LoadEmbeddedClientAssets() {
            return [
                LoadEmbeddedClientAsset("/", "index.html", "text/html"),
                LoadEmbeddedClientAsset("/app.js", "app.js", "text/javascript"),
                LoadEmbeddedClientAsset("/styles.css", "styles.css", "text/css")
            ];
        }

        /// <summary>
        /// Reads one embedded client asset and computes its stable ETag.
        /// </summary>
        /// <param name="routeSuffix"></param>
        /// <param name="fileName"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static ClientAsset LoadEmbeddedClientAsset(string routeSuffix, string fileName, string contentType) {
            string resourceName = $"SimpleW.Service.FileBrowser.Client.{fileName}";
            using Stream stream = typeof(FileBrowserModule).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded FileBrowser client resource '{resourceName}' was not found.");
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            byte[] data = buffer.ToArray();
            string etag = $"\"{Convert.ToHexString(SHA256.HashData(data))}\"";
            return new ClientAsset(routeSuffix, contentType, data, etag);
        }

        /// <summary>
        /// Checks whether an If-None-Match header contains the current asset ETag.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="etag"></param>
        /// <returns></returns>
        private static bool IfNoneMatchHasMatch(HttpRequest request, string etag) {
            if (!request.Headers.TryGetValue("If-None-Match", out string? value) || string.IsNullOrWhiteSpace(value)) {
                return false;
            }

            foreach (string raw in value.Split(',')) {
                string candidate = raw.Trim();
                if (candidate == "*" || string.Equals(candidate, etag, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        #endregion user interface

        #region operation queue

        /// <summary>
        /// Starts the single-reader background operation queue.
        /// </summary>
        private void StartOperations() {
            if (_operationsTask != null) {
                return;
            }

            _operationsCts = new CancellationTokenSource();
            CancellationToken cancellationToken = _operationsCts.Token;
            _operationsTask = Task.Run(() => RunOperationsAsync(cancellationToken));
            _uploadCleanupTask = Task.Run(() => RunUploadCleanupAsync(cancellationToken));
        }

        /// <summary>
        /// Cancels active work, cleans upload sessions and stops the operation queue.
        /// </summary>
        private async Task StopOperationsAsync() {
            CancellationTokenSource? cts = _operationsCts;
            Task? task = _operationsTask;
            Task? uploadCleanupTask = _uploadCleanupTask;
            _operationsCts = null;
            _operationsTask = null;
            _uploadCleanupTask = null;

            if (cts == null) {
                return;
            }

            try {
                foreach (QueuedOperation operation in _trackedOperations.Values) {
                    operation.Cancel();
                }
                cts.Cancel();

                foreach (KeyValuePair<Guid, UploadSession> item in _uploads.ToArray()) {
                    if (!TryRemoveUpload(item.Key, out UploadSession upload)) {
                        continue;
                    }

                    upload.Cancel();
                    await CleanupUploadSessionAsync(upload).ConfigureAwait(false);
                    upload.Dispose();
                }

                if (task != null && uploadCleanupTask != null) {
                    await Task.WhenAll(task, uploadCleanupTask).ConfigureAwait(false);
                }
                else if (task != null) {
                    await task.ConfigureAwait(false);
                }
                else if (uploadCleanupTask != null) {
                    await uploadCleanupTask.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally {
                cts.Dispose();
            }
        }

        /// <summary>
        /// Periodically expires inactive upload sessions and removes orphaned upload parts.
        /// </summary>
        /// <param name="cancellationToken"></param>
        private async Task RunUploadCleanupAsync(CancellationToken cancellationToken) {
            long intervalTicks = Math.Min(_options.UploadSessionTimeout.Ticks / 2, TimeSpan.FromMinutes(1).Ticks);
            TimeSpan interval = TimeSpan.FromTicks(Math.Max(intervalTicks, TimeSpan.FromSeconds(1).Ticks));
            using PeriodicTimer timer = new(interval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                try {
                    await CleanupExpiredUploadsAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
                    _log.Warn("upload cleanup failed", ex);
                }
            }
        }

        /// <summary>
        /// Executes queued operations sequentially and publishes their lifecycle events.
        /// </summary>
        /// <param name="cancellationToken"></param>
        private async Task RunOperationsAsync(CancellationToken cancellationToken) {
            await foreach (QueuedOperation operation in _operations.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                if (operation.Token.IsCancellationRequested) {
                    operation.MarkCancelled();
                    await PublishOperationCancelledAsync(operation).ConfigureAwait(false);
                    _trackedOperations.TryRemove(operation.Id, out _);
                    operation.Dispose();
                    continue;
                }

                operation.MarkRunning();
                await PublishEventAsync("filebrowser.operation.started", new {
                    operationId = operation.Id,
                    operation = operation.Kind,
                    path = operation.Path,
                    parentPath = ParentRelative(operation.Path),
                    timestampUtc = DateTimeOffset.UtcNow
                }).ConfigureAwait(false);

                try {
                    operation.Token.ThrowIfCancellationRequested();
                    OperationResult result = operation.Work(operation.Token);
                    operation.MarkCompleted();

                    await PublishEventAsync("filebrowser.operation.completed", new {
                        operationId = operation.Id,
                        operation = operation.Kind,
                        path = operation.Path,
                        payload = result.Payload,
                        timestampUtc = DateTimeOffset.UtcNow
                    }).ConfigureAwait(false);

                    foreach (object uploadCompletedPayload in result.UploadCompletedPayloads ?? Array.Empty<object>()) {
                        await PublishEventAsync("filebrowser.upload.completed", uploadCompletedPayload).ConfigureAwait(false);
                    }

                    foreach (string changedPath in result.ChangedPaths) {
                        await PublishChangedAsync(operation.Id, operation.Kind, changedPath).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (operation.Token.IsCancellationRequested) {
                    operation.MarkCancelled();
                    await PublishOperationCancelledAsync(operation).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    operation.MarkFailed();
                    await PublishEventAsync("filebrowser.operation.failed", new {
                        operationId = operation.Id,
                        operation = operation.Kind,
                        path = operation.Path,
                        error = ex.Message,
                        timestampUtc = DateTimeOffset.UtcNow
                    }).ConfigureAwait(false);
                }
                finally {
                    _trackedOperations.TryRemove(operation.Id, out _);
                    operation.Dispose();
                }
            }
        }

        /// <summary>
        /// Adds a file-system operation to the queue and returns its tracking identifier.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="kind"></param>
        /// <param name="path"></param>
        /// <param name="work"></param>
        /// <returns></returns>
        private ValueTask EnqueueOperationAsync(HttpSession session, string kind, string path, Func<CancellationToken, OperationResult> work) {
            Guid operationId = Guid.NewGuid();
            QueuedOperation operation = new(operationId, kind, path, work);
            if (!_trackedOperations.TryAdd(operationId, operation)) {
                operation.Dispose();
                return ErrorAsync(session, 503, "operation_queue_unavailable");
            }
            if (!_operations.Writer.TryWrite(operation)) {
                _trackedOperations.TryRemove(operationId, out _);
                operation.Dispose();
                return ErrorAsync(session, 503, "operation_queue_unavailable");
            }

            return JsonAsync(session, 202, new {
                ok = true,
                operationId,
                operation = kind,
                path
            });
        }

        #endregion operation queue

        #region events

        /// <summary>
        /// Publishes the cancellation event for a queued operation.
        /// </summary>
        /// <param name="operation"></param>
        private ValueTask PublishOperationCancelledAsync(QueuedOperation operation) {
            return PublishEventAsync("filebrowser.operation.cancelled", new {
                operationId = operation.Id,
                operation = operation.Kind,
                path = operation.Path,
                parentPath = ParentRelative(operation.Path),
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// Publishes a file browser change event for a modified path.
        /// </summary>
        /// <param name="operationId"></param>
        /// <param name="operation"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private ValueTask PublishChangedAsync(Guid operationId, string operation, string path) {
            return PublishEventAsync("filebrowser.changed", new {
                operationId,
                operation,
                path,
                parentPath = ParentRelative(path),
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// Publishes the latest progress for one uploaded file.
        /// </summary>
        /// <param name="uploadId"></param>
        /// <param name="file"></param>
        /// <returns></returns>
        private ValueTask PublishUploadProgressAsync(Guid uploadId, UploadFileState file) {
            return PublishEventAsync("filebrowser.upload.progress", new {
                uploadId,
                path = file.RelativePath,
                receivedBytes = file.ReceivedBytes,
                totalBytes = file.Size,
                completed = file.IsComplete,
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// Broadcasts an event when SSE support is enabled and initialized.
        /// </summary>
        /// <param name="eventName"></param>
        /// <param name="payload"></param>
        /// <returns></returns>
        private async ValueTask PublishEventAsync(string eventName, object payload) {
            if (!_options.EnableEvents || _eventsHub == null) {
                return;
            }

            await _eventsHub.BroadcastTextAsync(EventsRoom, SerializeEvent(payload), @event: eventName).ConfigureAwait(false);
        }

        /// <summary>
        /// Serializes an SSE event payload with the module JSON settings.
        /// </summary>
        /// <param name="payload"></param>
        private static string SerializeEvent(object payload) {
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        #endregion events

        #region configuration and listing

        /// <summary>
        /// Returns the client-facing module configuration.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask ConfigAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            return JsonAsync(session, 200, new {
                ok = true,
                prefix = _options.NormalizedPrefix,
                apiPrefix = Route("/api"),
                eventsPrefix = _options.EnableEvents ? _options.NormalizedEventsPrefix : null,
                enableEvents = _options.EnableEvents,
                uploadChunkThresholdBytes = _options.UploadChunkThresholdBytes,
                uploadChunkBytes = _options.UploadChunkBytes,
                uploadSessionTimeoutSeconds = _options.UploadSessionTimeout.TotalSeconds,
                maxConcurrentUploadSessions = _options.MaxConcurrentUploadSessions,
                maxFileBytes = _options.MaxFileBytes,
                maxUploadBytes = _options.MaxUploadBytes,
                maxExtractedFileBytes = _options.MaxExtractedFileBytes,
                maxExtractedBytes = _options.MaxExtractedBytes,
                maxArchiveEntries = _options.MaxArchiveEntries,
                defaultPageSize = _options.DefaultPageSize,
                maxPageSize = _options.MaxPageSize
            });
        }

        /// <summary>
        /// Lists one filtered, sorted and paginated directory page.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask ListAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            session.Request.Query.TryGetValue("path", out string? rawPath);
            if (!TryResolve(rawPath, allowRoot: true, mustBeRelativeToRoot: true, out ResolvedPath resolved, out string? error)) {
                return ErrorAsync(session, 400, error);
            }

            if (!Directory.Exists(resolved.FullPath)) {
                return ErrorAsync(session, 404, "directory_not_found");
            }

            string search = session.Request.Query.TryGetValue("search", out string? rawSearch)
                ? (rawSearch ?? string.Empty).Trim()
                : string.Empty;
            string sort = session.Request.Query.TryGetValue("sort", out string? rawSort) && !string.IsNullOrWhiteSpace(rawSort)
                ? rawSort.Trim().ToLowerInvariant()
                : "name";
            if (sort != "name" && sort != "size" && sort != "modified") {
                return ErrorAsync(session, 400, "invalid_sort");
            }

            string direction = session.Request.Query.TryGetValue("direction", out string? rawDirection) && !string.IsNullOrWhiteSpace(rawDirection)
                ? rawDirection.Trim().ToLowerInvariant()
                : "asc";
            if (direction != "asc" && direction != "desc") {
                return ErrorAsync(session, 400, "invalid_direction");
            }

            int pageSize = _options.DefaultPageSize;
            if (session.Request.Query.TryGetValue("pageSize", out string? rawPageSize)) {
                if (!int.TryParse(rawPageSize, NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                    || pageSize <= 0
                    || pageSize > _options.MaxPageSize) {
                    return ErrorAsync(session, 400, "invalid_page_size");
                }
            }

            BrowserItem? cursorItem = null;
            if (session.Request.Query.TryGetValue("continuationToken", out string? rawContinuationToken)
                && !string.IsNullOrWhiteSpace(rawContinuationToken)) {
                if (!TryDecodeContinuationToken(
                    rawContinuationToken,
                    resolved.RelativePath,
                    search,
                    sort,
                    direction,
                    pageSize,
                    out cursorItem
                )) {
                    return ErrorAsync(session, 400, "invalid_continuation_token");
                }
            }

            BrowserItemComparer comparer = new(sort, direction == "desc");
            List<BrowserItem> items = new(pageSize + 1);
            if (!TryEnsureNoReparsePoints(resolved.FullPath, out error)) {
                return ErrorAsync(session, 400, error);
            }
            foreach (string directory in Directory.EnumerateDirectories(resolved.FullPath)) {
                if (IsInternalPath(directory) || !TryEnsureNoReparsePoints(directory, out _)) {
                    continue;
                }
                DirectoryInfo info = new(directory);
                if (search.Length == 0 || info.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) {
                    AddPageCandidate(
                        items,
                        new BrowserItem(info.Name, CombineRelative(resolved.RelativePath, info.Name), "directory", 0, info.LastWriteTimeUtc),
                        cursorItem,
                        pageSize + 1,
                        comparer
                    );
                }
            }
            foreach (string file in Directory.EnumerateFiles(resolved.FullPath)) {
                if (IsInternalPath(file) || !TryEnsureNoReparsePoints(file, out _)) {
                    continue;
                }
                FileInfo info = new(file);
                if (search.Length == 0 || info.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) {
                    AddPageCandidate(
                        items,
                        new BrowserItem(info.Name, CombineRelative(resolved.RelativePath, info.Name), "file", info.Length, info.LastWriteTimeUtc),
                        cursorItem,
                        pageSize + 1,
                        comparer
                    );
                }
            }

            bool isTruncated = items.Count > pageSize;
            if (isTruncated) {
                items.RemoveAt(items.Count - 1);
            }
            string? nextContinuationToken = isTruncated && items.Count > 0
                ? EncodeContinuationToken(resolved.RelativePath, search, sort, direction, pageSize, items[^1])
                : null;

            return JsonAsync(session, 200, new {
                ok = true,
                path = resolved.RelativePath,
                parent = ParentRelative(resolved.RelativePath),
                search,
                sort,
                direction,
                pageSize,
                keyCount = items.Count,
                isTruncated,
                nextContinuationToken,
                items = items.Select(static i => new {
                    name = i.Name,
                    path = i.Path,
                    type = i.Type,
                    size = i.Size,
                    modifiedUtc = i.ModifiedUtc
                }).ToArray()
            });
        }

        /// <summary>
        /// Inserts a listing candidate into the bounded page buffer.
        /// </summary>
        /// <param name="items"></param>
        /// <param name="item"></param>
        /// <param name="cursorItem"></param>
        /// <param name="capacity"></param>
        /// <param name="comparer"></param>
        private static void AddPageCandidate(
            List<BrowserItem> items,
            BrowserItem item,
            BrowserItem? cursorItem,
            int capacity,
            BrowserItemComparer comparer
        ) {
            if (cursorItem != null && comparer.Compare(item, cursorItem) <= 0) {
                return;
            }

            int index = items.BinarySearch(item, comparer);
            if (index < 0) {
                index = ~index;
            }
            if (index >= capacity) {
                return;
            }

            items.Insert(index, item);
            if (items.Count > capacity) {
                items.RemoveAt(capacity);
            }
        }

        /// <summary>
        /// Encodes the last returned item and current query settings into a continuation token.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="search"></param>
        /// <param name="sort"></param>
        /// <param name="direction"></param>
        /// <param name="pageSize"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        private static string EncodeContinuationToken(
            string path,
            string search,
            string sort,
            string direction,
            int pageSize,
            BrowserItem item
        ) {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(new ListContinuationToken(
                1,
                path,
                search,
                sort,
                direction,
                pageSize,
                item.Name,
                item.Path,
                item.Type,
                item.Size,
                item.ModifiedUtc.Ticks
            ));
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Validates and decodes a continuation token for the current listing query.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="path"></param>
        /// <param name="search"></param>
        /// <param name="sort"></param>
        /// <param name="direction"></param>
        /// <param name="pageSize"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        private static bool TryDecodeContinuationToken(
            string token,
            string path,
            string search,
            string sort,
            string direction,
            int pageSize,
            out BrowserItem? item
        ) {
            item = null;
            try {
                string base64 = token.Replace('-', '+').Replace('_', '/');
                int padding = base64.Length % 4;
                if (padding == 1) {
                    return false;
                }
                if (padding > 0) {
                    base64 = base64.PadRight(base64.Length + 4 - padding, '=');
                }

                ListContinuationToken? value = JsonSerializer.Deserialize<ListContinuationToken>(Convert.FromBase64String(base64));
                if (value == null
                    || value.Version != 1
                    || !string.Equals(value.Path, path, StringComparison.Ordinal)
                    || !string.Equals(value.Search, search, StringComparison.Ordinal)
                    || !string.Equals(value.Sort, sort, StringComparison.Ordinal)
                    || !string.Equals(value.Direction, direction, StringComparison.Ordinal)
                    || value.PageSize != pageSize
                    || (value.Type != "directory" && value.Type != "file")
                    || string.IsNullOrEmpty(value.Name)
                    || string.IsNullOrEmpty(value.ItemPath)) {
                    return false;
                }

                item = new BrowserItem(
                    value.Name,
                    value.ItemPath,
                    value.Type,
                    value.Size,
                    new DateTime(value.ModifiedUtcTicks, DateTimeKind.Utc)
                );
                return true;
            }
            catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException) {
                return false;
            }
        }

        #endregion configuration and listing

        #region file system endpoints

        /// <summary>
        /// Sends a validated file as a downloadable response.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask DownloadAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            session.Request.Query.TryGetValue("path", out string? rawPath);
            if (!TryResolve(rawPath, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath resolved, out string? error)) {
                return ErrorAsync(session, 400, error);
            }

            FileInfo file = new(resolved.FullPath);
            if (!file.Exists) {
                return ErrorAsync(session, 404, "file_not_found");
            }
            if (!TryEnsureNoReparsePoints(resolved.FullPath, out error)) {
                return ErrorAsync(session, 400, error);
            }

            return session.Response
                          .Status(200)
                          .File(file)
                          .Attachment(file.Name)
                          .SendAsync();
        }

        /// <summary>
        /// Validates and queues the creation of a directory.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask CreateFolderAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            FolderRequest? request = ReadJson<FolderRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (!TryResolve(request.Path, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath resolved, out string? error)) {
                return ErrorAsync(session, 400, error);
            }
            if (File.Exists(resolved.FullPath) || Directory.Exists(resolved.FullPath)) {
                return ErrorAsync(session, 409, "destination_exists");
            }

            return EnqueueOperationAsync(session, "createFolder", resolved.RelativePath, cancellationToken => {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(resolved.FullPath) || Directory.Exists(resolved.FullPath)) {
                    throw new IOException("destination_exists");
                }

                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(resolved.FullPath);
                Directory.CreateDirectory(resolved.FullPath);
                return new OperationResult(
                    ChangedPaths: [ParentRelative(resolved.RelativePath)],
                    Payload: new { path = resolved.RelativePath }
                );
            });
        }

        /// <summary>
        /// Validates and queues a file-system entry rename.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask RenameAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            RenameRequest? request = ReadJson<RenameRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (!TryResolve(request.Path, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath source, out string? sourceError)) {
                return ErrorAsync(session, 400, sourceError);
            }
            if (!TryValidateName(request.Name, out string? nameError)) {
                return ErrorAsync(session, 400, nameError);
            }

            string parent = System.IO.Path.GetDirectoryName(source.FullPath) ?? _options.NormalizedPath;
            string destinationFull = System.IO.Path.Combine(parent, request.Name!.Trim());
            if (!TryEnsureInsideRoot(destinationFull)) {
                return ErrorAsync(session, 400, "invalid_path");
            }
            if (File.Exists(destinationFull) || Directory.Exists(destinationFull)) {
                return ErrorAsync(session, 409, "destination_exists");
            }

            if (!File.Exists(source.FullPath) && !Directory.Exists(source.FullPath)) {
                return ErrorAsync(session, 404, "source_not_found");
            }

            string destinationRelative = CombineRelative(ParentRelative(source.RelativePath), request.Name!.Trim());
            return EnqueueOperationAsync(session, "rename", source.RelativePath, cancellationToken => {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destinationFull) || Directory.Exists(destinationFull)) {
                    throw new IOException("destination_exists");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(source.FullPath)) {
                    EnsureNoReparsePoints(source.FullPath);
                    EnsureNoReparsePoints(destinationFull);
                    File.Move(source.FullPath, destinationFull);
                }
                else if (Directory.Exists(source.FullPath)) {
                    EnsureNoReparsePoints(source.FullPath);
                    EnsureNoReparsePoints(destinationFull);
                    Directory.Move(source.FullPath, destinationFull);
                }
                else {
                    throw new FileNotFoundException("source_not_found", source.FullPath);
                }

                return new OperationResult(
                    ChangedPaths: [ParentRelative(source.RelativePath)],
                    Payload: new { sourcePath = source.RelativePath, destinationPath = destinationRelative }
                );
            });
        }

        /// <summary>
        /// Validates and queues a file or directory move.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask MoveAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            MoveRequest? request = ReadJson<MoveRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (!TryResolve(request.SourcePath, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath source, out string? sourceError)) {
                return ErrorAsync(session, 400, sourceError);
            }
            if (!TryResolve(request.DestinationDirectory, allowRoot: true, mustBeRelativeToRoot: true, out ResolvedPath destinationDirectory, out string? destinationError)) {
                return ErrorAsync(session, 400, destinationError);
            }
            if (!Directory.Exists(destinationDirectory.FullPath)) {
                return ErrorAsync(session, 404, "destination_directory_not_found");
            }

            string name = string.IsNullOrWhiteSpace(request.Name) ? System.IO.Path.GetFileName(source.FullPath) : request.Name!.Trim();
            if (!TryValidateName(name, out string? nameError)) {
                return ErrorAsync(session, 400, nameError);
            }

            string destinationFull = System.IO.Path.Combine(destinationDirectory.FullPath, name);
            if (!TryEnsureInsideRoot(destinationFull)) {
                return ErrorAsync(session, 400, "invalid_path");
            }
            if (File.Exists(destinationFull) || Directory.Exists(destinationFull)) {
                return ErrorAsync(session, 409, "destination_exists");
            }

            bool isDirectory = Directory.Exists(source.FullPath);
            if (!isDirectory && !File.Exists(source.FullPath)) {
                return ErrorAsync(session, 404, "source_not_found");
            }
            if (isDirectory && IsInsideOrEqual(destinationDirectory.FullPath, source.FullPath)) {
                return ErrorAsync(session, 409, "cannot_move_directory_into_itself");
            }

            string destinationRelative = CombineRelative(destinationDirectory.RelativePath, name);
            return EnqueueOperationAsync(session, "move", source.RelativePath, cancellationToken => {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destinationFull) || Directory.Exists(destinationFull)) {
                    throw new IOException("destination_exists");
                }

                bool isDirectoryNow = Directory.Exists(source.FullPath);
                if (!isDirectoryNow && !File.Exists(source.FullPath)) {
                    throw new FileNotFoundException("source_not_found", source.FullPath);
                }
                if (isDirectoryNow && IsInsideOrEqual(destinationDirectory.FullPath, source.FullPath)) {
                    throw new IOException("cannot_move_directory_into_itself");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (isDirectoryNow) {
                    EnsureNoReparsePoints(source.FullPath);
                    EnsureNoReparsePoints(destinationFull);
                    Directory.Move(source.FullPath, destinationFull);
                }
                else {
                    EnsureNoReparsePoints(source.FullPath);
                    EnsureNoReparsePoints(destinationFull);
                    File.Move(source.FullPath, destinationFull);
                }

                return new OperationResult(
                    ChangedPaths: [ParentRelative(source.RelativePath), destinationDirectory.RelativePath],
                    Payload: new { sourcePath = source.RelativePath, destinationPath = destinationRelative }
                );
            });
        }

        /// <summary>
        /// Validates and queues one or more entries for transfer to the trash.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask DeleteAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            DeleteRequest? request = ReadJson<DeleteRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }

            List<string> rawPaths = new();
            if (!string.IsNullOrWhiteSpace(request.Path)) {
                rawPaths.Add(request.Path!);
            }
            if (request.Paths != null) {
                foreach (string? path in request.Paths) {
                    if (!string.IsNullOrWhiteSpace(path)) {
                        rawPaths.Add(path);
                    }
                }
            }
            if (rawPaths.Count == 0) {
                return ErrorAsync(session, 400, "path_required");
            }

            if (!TryEnsureNoTrashReparsePoints(_options.NormalizedTrashPath, out string? trashError)) {
                return ErrorAsync(session, 400, trashError);
            }
            Directory.CreateDirectory(_options.NormalizedTrashPath);
            if (!TryEnsureNoTrashReparsePoints(_options.NormalizedTrashPath, out trashError)) {
                return ErrorAsync(session, 400, trashError);
            }
            List<ResolvedPath> sources = new();
            foreach (string rawPath in rawPaths.Distinct(StringComparer.Ordinal)) {
                if (!TryResolve(rawPath, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath source, out string? sourceError)) {
                    return ErrorAsync(session, 400, sourceError);
                }
                if (!File.Exists(source.FullPath) && !Directory.Exists(source.FullPath)) {
                    return ErrorAsync(session, 404, "source_not_found");
                }

                sources.Add(source);
            }

            string operationPath = sources.Count == 1 ? sources[0].RelativePath : ParentRelative(sources[0].RelativePath);
            return EnqueueOperationAsync(session, "delete", operationPath, cancellationToken => {
                List<object> deleted = new();
                List<string> changedPaths = new();

                foreach (ResolvedPath source in sources) {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoReparsePoints(source.FullPath);
                    if (!File.Exists(source.FullPath) && !Directory.Exists(source.FullPath)) {
                        throw new FileNotFoundException("source_not_found", source.FullPath);
                    }

                    TrashEntry trashEntry = MoveEntryToTrash(source, cancellationToken);
                    deleted.Add(new { path = source.RelativePath, trashId = trashEntry.Id });
                    changedPaths.Add(ParentRelative(source.RelativePath));
                }

                return new OperationResult(
                    ChangedPaths: changedPaths.Distinct(StringComparer.Ordinal).ToArray(),
                    Payload: new { deleted }
                );
            });
        }

        #endregion file system endpoints

        #region trash

        /// <summary>
        /// Returns all managed and legacy entries currently stored in the trash.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask ListTrashAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            if (!TryEnsureNoTrashReparsePoints(_options.NormalizedTrashPath, out string? trashError)) {
                return ErrorAsync(session, 400, trashError);
            }
            Directory.CreateDirectory(_options.NormalizedTrashPath);
            if (!TryEnsureNoTrashReparsePoints(_options.NormalizedTrashPath, out trashError)) {
                return ErrorAsync(session, 400, trashError);
            }
            TrashEntry[] entries = ListTrashEntries()
                .OrderByDescending(entry => entry.DeletedUtc)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return JsonAsync(session, 200, new {
                ok = true,
                count = entries.Length,
                items = entries.Select(entry => new {
                    id = entry.Id,
                    name = entry.Name,
                    originalPath = entry.OriginalPath,
                    type = entry.Type,
                    size = entry.Size,
                    deletedUtc = entry.DeletedUtc,
                    canRestore = entry.CanRestore,
                    canRestoreElsewhere = entry.CanRestoreElsewhere
                }).ToArray()
            });
        }

        /// <summary>
        /// Validates and queues restoration of entries from the trash.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask RestoreTrashAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }
            TrashRequest? request = ReadJson<TrashRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            bool restoreElsewhere = !string.IsNullOrWhiteSpace(request.DestinationPath);
            if (!TryResolveTrashEntries(request.Ids, requireRestorable: !restoreElsewhere, out List<TrashEntry> entries, out string? trashError)) {
                return ErrorAsync(session, trashError == "trash_item_not_found" ? 404 : 400, trashError);
            }
            if (restoreElsewhere && entries.Count != 1) {
                return ErrorAsync(session, 400, "single_trash_item_required");
            }
            if (restoreElsewhere && !entries[0].CanRestoreElsewhere) {
                return ErrorAsync(session, 409, "trash_item_cannot_be_restored");
            }

            List<TrashRestorePlan> plans = new(entries.Count);
            foreach (TrashEntry entry in entries) {
                string? destinationPath = restoreElsewhere ? request.DestinationPath : entry.OriginalPath;
                if (!TryResolve(destinationPath, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath destination, out string? destinationError)) {
                    return ErrorAsync(session, 400, destinationError);
                }
                if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                    return ErrorAsync(session, 409, "destination_exists");
                }
                plans.Add(new TrashRestorePlan(entry, destination));
            }

            string operationPath = plans.Count == 1 ? plans[0].Destination.RelativePath : ParentRelative(plans[0].Destination.RelativePath);
            return EnqueueOperationAsync(session, "restore", operationPath, cancellationToken => {
                List<object> restored = new();
                List<string> changedPaths = new();
                foreach (TrashRestorePlan plan in plans) {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoTrashReparsePoints(plan.Entry.PayloadPath);
                    if (!File.Exists(plan.Entry.PayloadPath) && !Directory.Exists(plan.Entry.PayloadPath)) {
                        throw new FileNotFoundException("trash_item_not_found", plan.Entry.PayloadPath);
                    }
                    if (File.Exists(plan.Destination.FullPath) || Directory.Exists(plan.Destination.FullPath)) {
                        throw new IOException("destination_exists");
                    }

                    string? destinationParent = System.IO.Path.GetDirectoryName(plan.Destination.FullPath);
                    if (!string.IsNullOrEmpty(destinationParent)) {
                        EnsureNoReparsePoints(destinationParent);
                        Directory.CreateDirectory(destinationParent);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoReparsePoints(plan.Destination.FullPath);
                    EnsureNoTrashReparsePoints(plan.Entry.PayloadPath);
                    if (plan.Entry.Type == "directory") {
                        Directory.Move(plan.Entry.PayloadPath, plan.Destination.FullPath);
                    }
                    else {
                        File.Move(plan.Entry.PayloadPath, plan.Destination.FullPath);
                    }

                    if (plan.Entry.CanRestore) {
                        string metadataPath = System.IO.Path.Combine(plan.Entry.RootPath, TrashMetadataFileName);
                        EnsureNoTrashReparsePoints(metadataPath);
                        TryDeleteFile(metadataPath);
                        EnsureNoTrashReparsePoints(plan.Entry.RootPath);
                        Directory.Delete(plan.Entry.RootPath, recursive: false);
                    }
                    restored.Add(new { id = plan.Entry.Id, path = plan.Destination.RelativePath });
                    changedPaths.Add(ParentRelative(plan.Destination.RelativePath));
                }

                return new OperationResult(
                    ChangedPaths: changedPaths.Distinct(StringComparer.Ordinal).ToArray(),
                    Payload: new { restored }
                );
            });
        }

        /// <summary>
        /// Validates and queues permanent deletion of selected trash entries.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask DeleteTrashAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }
            TrashRequest? request = ReadJson<TrashRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (!TryResolveTrashEntries(request.Ids, requireRestorable: false, out List<TrashEntry> entries, out string? trashError)) {
                return ErrorAsync(session, trashError == "trash_item_not_found" ? 404 : 400, trashError);
            }

            return EnqueueOperationAsync(session, "purge", "Trash", cancellationToken =>
                PermanentlyDeleteTrashEntries(entries, cancellationToken));
        }

        /// <summary>
        /// Queues permanent deletion of every trash entry.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask EmptyTrashAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            if (!TryEnsureNoTrashReparsePoints(_options.NormalizedTrashPath, out string? trashError)) {
                return ErrorAsync(session, 400, trashError);
            }
            Directory.CreateDirectory(_options.NormalizedTrashPath);
            if (!TryEnsureNoTrashReparsePoints(_options.NormalizedTrashPath, out trashError)) {
                return ErrorAsync(session, 400, trashError);
            }
            return EnqueueOperationAsync(session, "emptyTrash", "Trash", cancellationToken =>
                PermanentlyDeleteTrashEntries(ListTrashEntries().ToList(), cancellationToken));
        }

        /// <summary>
        /// Permanently removes resolved trash entries from disk.
        /// </summary>
        /// <param name="entries"></param>
        /// <param name="cancellationToken"></param>
        private OperationResult PermanentlyDeleteTrashEntries(IReadOnlyList<TrashEntry> entries, CancellationToken cancellationToken) {
            List<string> deletedIds = new(entries.Count);
            foreach (TrashEntry entry in entries) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(entry.RootPath) && !Directory.Exists(entry.RootPath)) {
                    continue;
                }
                EnsureNoTrashReparsePoints(entry.RootPath);
                DeleteFileSystemEntry(entry.RootPath, cancellationToken);
                deletedIds.Add(entry.Id);
            }
            return new OperationResult(
                ChangedPaths: Array.Empty<string>(),
                Payload: new { deletedIds }
            );
        }

        /// <summary>
        /// Moves one entry into an isolated trash directory and writes restoration metadata.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="cancellationToken"></param>
        private TrashEntry MoveEntryToTrash(ResolvedPath source, CancellationToken cancellationToken) {
            bool isDirectory = Directory.Exists(source.FullPath);
            DateTimeOffset deletedUtc = DateTimeOffset.UtcNow;
            string id = $"{deletedUtc:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            string rootPath = System.IO.Path.Combine(_options.NormalizedTrashPath, id);
            string payloadPath = System.IO.Path.Combine(rootPath, TrashPayloadName);
            string name = System.IO.Path.GetFileName(TrimEndingDirectorySeparator(source.FullPath));
            TrashItemMetadata metadata = new(1, source.RelativePath, name, isDirectory ? "directory" : "file", deletedUtc);

            EnsureNoTrashReparsePoints(rootPath);
            Directory.CreateDirectory(rootPath);
            EnsureNoTrashReparsePoints(rootPath);
            try {
                string metadataPath = System.IO.Path.Combine(rootPath, TrashMetadataFileName);
                EnsureNoTrashReparsePoints(metadataPath);
                File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(source.FullPath);
                EnsureNoTrashReparsePoints(payloadPath);
                if (isDirectory) {
                    Directory.Move(source.FullPath, payloadPath);
                }
                else {
                    File.Move(source.FullPath, payloadPath);
                }
            }
            catch {
                string metadataPath = System.IO.Path.Combine(rootPath, TrashMetadataFileName);
                if (TryEnsureNoTrashReparsePoints(metadataPath, out _)) {
                    TryDeleteFile(metadataPath);
                }
                try {
                    if (TryEnsureNoTrashReparsePoints(rootPath, out _)) {
                        Directory.Delete(rootPath, recursive: false);
                    }
                }
                catch {
                    // Keep any successfully moved payload in trash rather than deleting user data.
                }
                throw;
            }

            long? size = isDirectory ? null : new FileInfo(payloadPath).Length;
            return new TrashEntry(id, rootPath, payloadPath, name, source.RelativePath, isDirectory ? "directory" : "file", size, deletedUtc, true, true);
        }

        /// <summary>
        /// Enumerates valid managed entries and compatible legacy entries from the trash directory.
        /// </summary>
        private IEnumerable<TrashEntry> ListTrashEntries() {
            EnsureNoTrashReparsePoints(_options.NormalizedTrashPath);
            foreach (string rootPath in Directory.EnumerateFileSystemEntries(_options.NormalizedTrashPath)) {
                if (!TryEnsureNoTrashReparsePoints(rootPath, out _)) {
                    continue;
                }
                string id = System.IO.Path.GetFileName(rootPath);
                if (TryReadManagedTrashEntry(id, rootPath, out TrashEntry managedEntry)) {
                    yield return managedEntry;
                    continue;
                }

                bool isDirectory = Directory.Exists(rootPath);
                long? size = isDirectory ? null : new FileInfo(rootPath).Length;
                DateTimeOffset deletedUtc = File.GetLastWriteTimeUtc(rootPath);
                yield return new TrashEntry(id, rootPath, rootPath, id, null, isDirectory ? "directory" : "file", size, deletedUtc, false, true);
            }
        }

        /// <summary>
        /// Reads and validates metadata for one managed trash entry.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="rootPath"></param>
        /// <param name="entry"></param>
        /// <returns></returns>
        private bool TryReadManagedTrashEntry(string id, string rootPath, out TrashEntry entry) {
            entry = default!;
            if (!TryEnsureNoTrashReparsePoints(rootPath, out _) || !Directory.Exists(rootPath)) {
                return false;
            }
            string metadataPath = System.IO.Path.Combine(rootPath, TrashMetadataFileName);
            if (!TryEnsureNoTrashReparsePoints(metadataPath, out _) || !File.Exists(metadataPath)) {
                return false;
            }

            try {
                TrashItemMetadata? metadata = JsonSerializer.Deserialize<TrashItemMetadata>(File.ReadAllText(metadataPath), JsonOptions);
                if (metadata == null
                    || metadata.Version != 1
                    || (metadata.Type != "file" && metadata.Type != "directory")
                    || string.IsNullOrWhiteSpace(metadata.Name)
                    || !TryResolve(metadata.OriginalPath, allowRoot: false, mustBeRelativeToRoot: true, out _, out _)) {
                    return false;
                }

                string payloadPath = System.IO.Path.Combine(rootPath, TrashPayloadName);
                if (!TryEnsureNoTrashReparsePoints(payloadPath, out _)) {
                    return false;
                }
                bool payloadExists = metadata.Type == "directory" ? Directory.Exists(payloadPath) : File.Exists(payloadPath);
                if (!payloadExists) {
                    return false;
                }
                long? size = metadata.Type == "file" ? new FileInfo(payloadPath).Length : null;
                entry = new TrashEntry(id, rootPath, payloadPath, metadata.Name, metadata.OriginalPath, metadata.Type, size, metadata.DeletedUtc, true, true);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) {
                return false;
            }
        }

        /// <summary>
        /// Resolves requested trash identifiers and optionally requires restoration metadata.
        /// </summary>
        /// <param name="ids"></param>
        /// <param name="requireRestorable"></param>
        /// <param name="entries"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private bool TryResolveTrashEntries(IReadOnlyList<string>? ids, bool requireRestorable, out List<TrashEntry> entries, out string? error) {
            entries = new List<TrashEntry>();
            error = null;
            if (ids == null || ids.Count == 0) {
                error = "trash_item_required";
                return false;
            }

            StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            foreach (string? rawId in ids.Distinct(comparer)) {
                if (!TryValidateName(rawId, out _)) {
                    error = "invalid_trash_item";
                    return false;
                }
                string id = rawId!.Trim();
                string rootPath = System.IO.Path.Combine(_options.NormalizedTrashPath, id);
                if (!TryEnsureNoTrashReparsePoints(rootPath, out error)) {
                    return false;
                }
                if (!File.Exists(rootPath) && !Directory.Exists(rootPath)) {
                    error = "trash_item_not_found";
                    return false;
                }

                TrashEntry entry;
                if (!TryReadManagedTrashEntry(id, rootPath, out entry)) {
                    bool isDirectory = Directory.Exists(rootPath);
                    long? size = isDirectory ? null : new FileInfo(rootPath).Length;
                    entry = new TrashEntry(id, rootPath, rootPath, id, null, isDirectory ? "directory" : "file", size, File.GetLastWriteTimeUtc(rootPath), false, true);
                }
                if (requireRestorable && !entry.CanRestore) {
                    error = "restore_location_unavailable";
                    return false;
                }
                entries.Add(entry);
            }
            return true;
        }

        /// <summary>
        /// Deletes a file or directory after observing cancellation.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="cancellationToken"></param>
        private static void DeleteFileSystemEntry(string path, CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0) {
                File.Delete(path);
                return;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0) {
                Directory.Delete(path, recursive: false);
                return;
            }

            foreach (string childPath in Directory.EnumerateFileSystemEntries(path)) {
                DeleteFileSystemEntry(childPath, cancellationToken);
            }
            Directory.Delete(path, recursive: false);
        }

        #endregion trash

        #region archives

        /// <summary>
        /// Validates and queues creation of a ZIP archive.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask ArchiveAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            ArchiveRequest? request = ReadJson<ArchiveRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (request.Paths == null || request.Paths.Count == 0) {
                return ErrorAsync(session, 400, "path_required");
            }

            List<ResolvedPath> sources = new();
            HashSet<string> sourcePaths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string? rawPath in request.Paths) {
                if (string.IsNullOrWhiteSpace(rawPath)) {
                    return ErrorAsync(session, 400, "path_required");
                }
                if (!TryResolve(rawPath, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath source, out string? sourceError)) {
                    return ErrorAsync(session, 400, sourceError);
                }
                if (!File.Exists(source.FullPath) && !Directory.Exists(source.FullPath)) {
                    return ErrorAsync(session, 404, "source_not_found");
                }
                if (sourcePaths.Add(source.FullPath)) {
                    sources.Add(source);
                }
            }

            if (!TryResolve(request.DestinationPath, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath destination, out string? destinationError)) {
                return ErrorAsync(session, 400, destinationError);
            }
            if (!string.Equals(System.IO.Path.GetExtension(destination.FullPath), ".zip", StringComparison.OrdinalIgnoreCase)) {
                return ErrorAsync(session, 400, "archive_extension_required");
            }
            if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                return ErrorAsync(session, 409, "destination_exists");
            }

            string destinationParent = System.IO.Path.GetDirectoryName(destination.FullPath) ?? _options.NormalizedPath;
            if (!Directory.Exists(destinationParent)) {
                return ErrorAsync(session, 404, "destination_directory_not_found");
            }

            for (int i = 0; i < sources.Count; i++) {
                ResolvedPath source = sources[i];
                if (string.Equals(source.FullPath, destination.FullPath, _pathComparison)) {
                    return ErrorAsync(session, 409, "archive_destination_is_source");
                }
                if (Directory.Exists(source.FullPath) && IsInsideOrEqual(destination.FullPath, source.FullPath)) {
                    return ErrorAsync(session, 409, "archive_destination_inside_source");
                }

                for (int j = i + 1; j < sources.Count; j++) {
                    ResolvedPath other = sources[j];
                    if ((Directory.Exists(source.FullPath) && IsInsideOrEqual(other.FullPath, source.FullPath))
                        || (Directory.Exists(other.FullPath) && IsInsideOrEqual(source.FullPath, other.FullPath))) {
                        return ErrorAsync(session, 409, "overlapping_sources");
                    }
                }
            }

            return EnqueueOperationAsync(session, "archive", destination.RelativePath, cancellationToken =>
                CreateArchive(sources, destination, cancellationToken));
        }

        /// <summary>
        /// Creates a ZIP archive from validated source paths.
        /// </summary>
        /// <param name="sources"></param>
        /// <param name="destination"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private OperationResult CreateArchive(IReadOnlyList<ResolvedPath> sources, ResolvedPath destination, CancellationToken cancellationToken) {
            string tempArchivePath = System.IO.Path.Combine(_tempPath, $"archive-{Guid.NewGuid():N}.zip");
            try {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                    throw new IOException("destination_exists");
                }

                HashSet<string> archiveEntryNames = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                byte[] buffer = new byte[81920];
                int entryCount = 0;
                EnsureNoReparsePoints(tempArchivePath);
                using (FileStream tempStream = new(tempArchivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive archive = new(tempStream, ZipArchiveMode.Create, leaveOpen: false)) {
                    foreach (ResolvedPath source in sources) {
                        cancellationToken.ThrowIfCancellationRequested();
                        EnsureNoReparsePoints(source.FullPath);
                        string rootName = System.IO.Path.GetFileName(TrimEndingDirectorySeparator(source.FullPath));
                        if (string.IsNullOrWhiteSpace(rootName)) {
                            throw new InvalidDataException("invalid_source_name");
                        }

                        if (File.Exists(source.FullPath)) {
                            AddFileToArchive(archive, source.FullPath, rootName, archiveEntryNames, buffer, ref entryCount, cancellationToken);
                        }
                        else if (Directory.Exists(source.FullPath)) {
                            AddDirectoryToArchive(archive, source.FullPath, rootName, archiveEntryNames, buffer, ref entryCount, cancellationToken);
                        }
                        else {
                            throw new FileNotFoundException("source_not_found", source.FullPath);
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                    throw new IOException("destination_exists");
                }
                EnsureNoReparsePoints(tempArchivePath);
                EnsureNoReparsePoints(destination.FullPath);
                File.Move(tempArchivePath, destination.FullPath);
                return new OperationResult(
                    ChangedPaths: [ParentRelative(destination.RelativePath)],
                    Payload: new {
                        paths = sources.Select(source => source.RelativePath).ToArray(),
                        destinationPath = destination.RelativePath,
                        entries = entryCount
                    }
                );
            }
            catch {
                if (TryEnsureNoReparsePoints(tempArchivePath, out _)) {
                    TryDeleteFile(tempArchivePath);
                }
                throw;
            }
        }

        /// <summary>
        /// Adds a directory tree to an archive while enforcing entry and path limits.
        /// </summary>
        /// <param name="archive"></param>
        /// <param name="directoryPath"></param>
        /// <param name="entryPath"></param>
        /// <param name="archiveEntryNames"></param>
        /// <param name="buffer"></param>
        /// <param name="entryCount"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="InvalidDataException"></exception>
        private void AddDirectoryToArchive(
            ZipArchive archive,
            string directoryPath,
            string entryPath,
            HashSet<string> archiveEntryNames,
            byte[] buffer,
            ref int entryCount,
            CancellationToken cancellationToken) {

            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(directoryPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidDataException("archive_reparse_point_forbidden");
            }

            string directoryEntryPath = entryPath.Replace('\\', '/').TrimEnd('/') + "/";
            AddArchiveEntryName(directoryEntryPath, archiveEntryNames, ref entryCount);
            archive.CreateEntry(directoryEntryPath);

            foreach (string childPath in Directory.EnumerateFileSystemEntries(directoryPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                cancellationToken.ThrowIfCancellationRequested();
                string childEntryPath = directoryEntryPath + System.IO.Path.GetFileName(childPath);
                FileAttributes childAttributes = File.GetAttributes(childPath);
                if ((childAttributes & FileAttributes.ReparsePoint) != 0) {
                    throw new InvalidDataException("archive_reparse_point_forbidden");
                }
                if ((childAttributes & FileAttributes.Directory) != 0) {
                    AddDirectoryToArchive(archive, childPath, childEntryPath, archiveEntryNames, buffer, ref entryCount, cancellationToken);
                }
                else {
                    AddFileToArchive(archive, childPath, childEntryPath, archiveEntryNames, buffer, ref entryCount, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Adds one file to an archive when its entry name has not already been used.
        /// </summary>
        /// <param name="archive"></param>
        /// <param name="filePath"></param>
        /// <param name="entryPath"></param>
        /// <param name="archiveEntryNames"></param>
        /// <param name="buffer"></param>
        /// <param name="entryCount"></param>
        /// <param name="cancellationToken"></param>
        /// <exception cref="InvalidDataException"></exception>
        private void AddFileToArchive(
            ZipArchive archive,
            string filePath,
            string entryPath,
            HashSet<string> archiveEntryNames,
            byte[] buffer,
            ref int entryCount,
            CancellationToken cancellationToken) {

            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidDataException("archive_reparse_point_forbidden");
            }

            string normalizedEntryPath = entryPath.Replace('\\', '/');
            AddArchiveEntryName(normalizedEntryPath, archiveEntryNames, ref entryCount);
            ZipArchiveEntry entry = archive.CreateEntry(normalizedEntryPath, CompressionLevel.Optimal);
            using FileStream input = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using Stream output = entry.Open();
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
        }

        /// <summary>
        /// Tracks a unique archive entry name and enforces the configured entry limit.
        /// </summary>
        /// <param name="entryPath"></param>
        /// <param name="archiveEntryNames"></param>
        /// <param name="entryCount"></param>
        /// <exception cref="InvalidDataException"></exception>
        private void AddArchiveEntryName(string entryPath, HashSet<string> archiveEntryNames, ref int entryCount) {
            if (!archiveEntryNames.Add(entryPath)) {
                throw new InvalidDataException("duplicate_archive_entry");
            }
            entryCount++;
            if (entryCount > _options.MaxArchiveEntries) {
                throw new InvalidDataException("archive_too_many_entries");
            }
        }

        /// <summary>
        /// Validates and queues extraction of a ZIP archive.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        private ValueTask ExtractAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            ExtractRequest? request = ReadJson<ExtractRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (!TryResolve(request.Path, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath source, out string? sourceError)) {
                return ErrorAsync(session, 400, sourceError);
            }
            if (!File.Exists(source.FullPath)) {
                return ErrorAsync(session, 404, "source_not_found");
            }
            if (!string.Equals(System.IO.Path.GetExtension(source.FullPath), ".zip", StringComparison.OrdinalIgnoreCase)) {
                return ErrorAsync(session, 400, "unsupported_archive");
            }
            if (!TryResolve(request.DestinationDirectory, allowRoot: true, mustBeRelativeToRoot: true, out ResolvedPath destination, out string? destinationError)) {
                return ErrorAsync(session, 400, destinationError);
            }

            if (request.CreateDestinationDirectory) {
                if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                    return ErrorAsync(session, 409, "destination_exists");
                }
            }
            else if (!Directory.Exists(destination.FullPath)) {
                return ErrorAsync(session, 404, "destination_directory_not_found");
            }

            return EnqueueOperationAsync(session, "extract", source.RelativePath, cancellationToken =>
                ExtractArchive(source, destination, request.CreateDestinationDirectory, cancellationToken));
        }

        /// <summary>
        /// Extracts validated ZIP entries while protecting against traversal and archive bombs.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="createDestinationDirectory"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="IOException"></exception>
        /// <exception cref="DirectoryNotFoundException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        private OperationResult ExtractArchive(ResolvedPath source, ResolvedPath destination, bool createDestinationDirectory, CancellationToken cancellationToken) {
            bool createdDestination = false;
            try {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(source.FullPath)) {
                    throw new FileNotFoundException("source_not_found", source.FullPath);
                }
                if (createDestinationDirectory) {
                    if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                        throw new IOException("destination_exists");
                    }
                }
                else if (!Directory.Exists(destination.FullPath)) {
                    throw new DirectoryNotFoundException("destination_directory_not_found");
                }

                EnsureNoReparsePoints(source.FullPath);
                EnsureNoReparsePoints(destination.FullPath);
                using ZipArchive archive = ZipFile.OpenRead(source.FullPath);
                if (archive.Entries.Count > _options.MaxArchiveEntries) {
                    throw new InvalidDataException("archive_too_many_entries");
                }

                StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                HashSet<string> plannedPaths = new(pathComparer);
                HashSet<string> plannedFiles = new(pathComparer);
                List<ArchiveEntryPlan> plan = new(archive.Entries.Count);
                long declaredTotal = 0;

                foreach (ZipArchiveEntry entry in archive.Entries) {
                    cancellationToken.ThrowIfCancellationRequested();
                    string archivePath = entry.FullName.Replace('\\', '/');
                    string[] segments = archivePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0) {
                        continue;
                    }
                    if (segments.Any(segment => segment == "." || segment == "..")) {
                        throw new InvalidDataException("archive_path_traversal_forbidden");
                    }

                    string entryFullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(destination.FullPath, System.IO.Path.Combine(segments)));
                    if (!IsInsideOrEqual(entryFullPath, destination.FullPath) || !TryEnsureInsideRoot(entryFullPath)) {
                        throw new InvalidDataException("archive_path_outside_destination");
                    }
                    if (!plannedPaths.Add(entryFullPath)) {
                        throw new InvalidDataException("duplicate_archive_entry");
                    }

                    bool isDirectory = archivePath.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name);
                    if (isDirectory) {
                        if (File.Exists(entryFullPath)) {
                            throw new IOException("destination_exists");
                        }
                    }
                    else {
                        if (entry.Length > _options.MaxExtractedFileBytes) {
                            throw new InvalidDataException("archive_entry_too_large");
                        }
                        if (entry.Length > _options.MaxExtractedBytes - declaredTotal) {
                            throw new InvalidDataException("archive_too_large");
                        }
                        declaredTotal += entry.Length;
                        if (File.Exists(entryFullPath) || Directory.Exists(entryFullPath)) {
                            throw new IOException("destination_exists");
                        }
                        plannedFiles.Add(entryFullPath);
                    }

                    plan.Add(new ArchiveEntryPlan(entry, entryFullPath, isDirectory));
                }

                foreach (ArchiveEntryPlan item in plan) {
                    string? parent = System.IO.Path.GetDirectoryName(item.FullPath);
                    while (!string.IsNullOrEmpty(parent) && !string.Equals(parent, destination.FullPath, _pathComparison)) {
                        if (plannedFiles.Contains(parent) || File.Exists(parent)) {
                            throw new InvalidDataException("archive_path_conflict");
                        }
                        parent = System.IO.Path.GetDirectoryName(parent);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (createDestinationDirectory) {
                    if (File.Exists(destination.FullPath) || Directory.Exists(destination.FullPath)) {
                        throw new IOException("destination_exists");
                    }
                    EnsureNoReparsePoints(destination.FullPath);
                    Directory.CreateDirectory(destination.FullPath);
                    EnsureNoReparsePoints(destination.FullPath);
                    createdDestination = true;
                }

                byte[] buffer = new byte[81920];
                long extractedTotal = 0;
                int extractedFiles = 0;
                foreach (ArchiveEntryPlan item in plan) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.IsDirectory) {
                        EnsureNoReparsePoints(item.FullPath);
                        Directory.CreateDirectory(item.FullPath);
                        EnsureNoReparsePoints(item.FullPath);
                        continue;
                    }

                    string parent = System.IO.Path.GetDirectoryName(item.FullPath) ?? destination.FullPath;
                    EnsureNoReparsePoints(parent);
                    Directory.CreateDirectory(parent);
                    EnsureNoReparsePoints(item.FullPath);
                    bool createdFile = false;
                    try {
                        using Stream input = item.Entry.Open();
                        using FileStream output = new(item.FullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                        createdFile = true;
                        long extractedEntry = 0;
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (read > _options.MaxExtractedFileBytes - extractedEntry) {
                                throw new InvalidDataException("archive_entry_too_large");
                            }
                            if (read > _options.MaxExtractedBytes - extractedTotal) {
                                throw new InvalidDataException("archive_too_large");
                            }
                            output.Write(buffer, 0, read);
                            extractedEntry += read;
                            extractedTotal += read;
                        }
                        extractedFiles++;
                    }
                    catch {
                        if (createdFile && TryEnsureNoReparsePoints(item.FullPath, out _)) {
                            TryDeleteFile(item.FullPath);
                        }
                        throw;
                    }
                }

                string changedPath = createDestinationDirectory
                    ? ParentRelative(destination.RelativePath)
                    : destination.RelativePath;
                return new OperationResult(
                    ChangedPaths: [changedPath],
                    Payload: new {
                        path = source.RelativePath,
                        destinationDirectory = destination.RelativePath,
                        createdDestinationDirectory = createDestinationDirectory,
                        extractedFiles,
                        extractedBytes = extractedTotal
                    }
                );
            }
            catch {
                if (createdDestination) {
                    try {
                        Directory.Delete(destination.FullPath, recursive: true);
                    }
                    catch {
                        // best effort rollback of the newly created extraction directory
                    }
                }
                throw;
            }
        }

        #endregion archives

        #region uploads

        /// <summary>
        /// Cancels all tracked file operations and active upload sessions.
        /// </summary>
        /// <param name="session"></param>
        private async ValueTask CancelOperationsAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                await ForbiddenAsync(session).ConfigureAwait(false);
                return;
            }

            int cancelledOperations = 0;
            int cancelledUploads = 0;

            foreach (QueuedOperation operation in _trackedOperations.Values) {
                if (operation.Cancel()) {
                    cancelledOperations++;
                }
            }

            foreach (KeyValuePair<Guid, UploadSession> item in _uploads.ToArray()) {
                if (!TryRemoveUpload(item.Key, out UploadSession upload)) {
                    continue;
                }

                bool cancelled = upload.Cancel();
                if (cancelled) {
                    cancelledUploads++;
                }
                try {
                    await CleanupUploadSessionAsync(upload).ConfigureAwait(false);
                    if (cancelled) {
                        await PublishUploadCancelledAsync(upload).ConfigureAwait(false);
                    }
                }
                finally {
                    upload.Dispose();
                }
            }

            await JsonAsync(session, 202, new {
                ok = true,
                cancelledOperations,
                cancelledUploads
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes the cancellation event for an upload session.
        /// </summary>
        /// <param name="upload"></param>
        /// <param name="reason"></param>
        private ValueTask PublishUploadCancelledAsync(UploadSession upload, string reason = "cancelled") {
            return PublishEventAsync("filebrowser.upload.cancelled", new {
                uploadId = upload.Id,
                files = upload.Files.Keys.ToArray(),
                reason,
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// Validates file declarations and creates a tracked upload session.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask CreateUploadAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }

            CreateUploadRequest? request = ReadJson<CreateUploadRequest>(session, out string? jsonError);
            if (request == null) {
                return ErrorAsync(session, 400, jsonError);
            }
            if (request.Files == null || request.Files.Count == 0) {
                return ErrorAsync(session, 400, "files_required");
            }

            long total = 0;
            Dictionary<string, UploadFileState> files = new(StringComparer.Ordinal);
            Guid uploadId = Guid.NewGuid();

            foreach (UploadFileRequest file in request.Files) {
                if (file == null || string.IsNullOrWhiteSpace(file.Path)) {
                    return ErrorAsync(session, 400, "file_path_required");
                }
                if (file.Size < 0) {
                    return ErrorAsync(session, 400, "invalid_file_size");
                }
                if (file.Size > _options.MaxFileBytes) {
                    return ErrorAsync(session, 413, "file_too_large");
                }

                total = checked(total + file.Size);
                if (total > _options.MaxUploadBytes) {
                    return ErrorAsync(session, 413, "upload_too_large");
                }
                if (!TryResolve(file.Path, allowRoot: false, mustBeRelativeToRoot: true, out ResolvedPath resolved, out string? error)) {
                    return ErrorAsync(session, 400, error);
                }
                if (files.ContainsKey(resolved.RelativePath)) {
                    return ErrorAsync(session, 409, "duplicate_file_path");
                }

                string tempFile = System.IO.Path.Combine(_tempPath, $"{uploadId:N}-{files.Count.ToString(CultureInfo.InvariantCulture)}.part");
                files.Add(resolved.RelativePath, new UploadFileState(resolved.RelativePath, resolved.FullPath, tempFile, file.Size));
            }

            UploadSession upload = new(uploadId, files, total);
            if (!TryAddUpload(upload)) {
                upload.Dispose();
                return ErrorAsync(session, 429, "too_many_upload_sessions");
            }

            return JsonAsync(session, 201, new {
                ok = true,
                uploadId,
                chunkThresholdBytes = _options.UploadChunkThresholdBytes,
                chunkBytes = _options.UploadChunkBytes,
                expiresAtUtc = upload.LastActivityAtUtc + _options.UploadSessionTimeout
            });
        }

        /// <summary>
        /// Returns the received ranges required to resume an active upload session.
        /// </summary>
        /// <param name="session"></param>
        private async ValueTask GetUploadAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                await ForbiddenAsync(session).ConfigureAwait(false);
                return;
            }
            if (!TryGetUploadId(session, out Guid uploadId)) {
                await ErrorAsync(session, 400, "invalid_upload_id").ConfigureAwait(false);
                return;
            }
            if (!_uploads.TryGetValue(uploadId, out UploadSession? upload) || !upload.TryTouch(_options.UploadSessionTimeout)) {
                await ErrorAsync(session, 404, "upload_not_found").ConfigureAwait(false);
                return;
            }

            List<object> files = new(upload.Files.Count);
            long receivedBytes = 0;
            bool readyToComplete = true;
            try {
                foreach (UploadFileState file in upload.Files.Values) {
                    await file.Gate.WaitAsync(upload.Token).ConfigureAwait(false);
                    try {
                        receivedBytes += file.ReceivedBytes;
                        readyToComplete &= file.IsComplete;
                        files.Add(new {
                            path = file.RelativePath,
                            size = file.Size,
                            receivedBytes = file.ReceivedBytes,
                            completed = file.IsComplete,
                            receivedRanges = file.GetReceivedRanges().Select(static range => new {
                                start = range.Start,
                                end = range.End
                            }).ToArray()
                        });
                    }
                    finally {
                        file.Gate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (upload.IsCancellationRequested) {
                await ErrorAsync(session, 404, "upload_not_found").ConfigureAwait(false);
                return;
            }

            upload.TryTouch(_options.UploadSessionTimeout);
            DateTimeOffset lastActivityAtUtc = upload.LastActivityAtUtc;
            await JsonAsync(session, 200, new {
                ok = true,
                uploadId = upload.Id,
                totalBytes = upload.TotalBytes,
                receivedBytes,
                chunkThresholdBytes = _options.UploadChunkThresholdBytes,
                chunkBytes = _options.UploadChunkBytes,
                createdAtUtc = upload.CreatedAtUtc,
                lastActivityAtUtc,
                expiresAtUtc = lastActivityAtUtc + _options.UploadSessionTimeout,
                readyToComplete,
                files
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancels an upload session and removes all of its temporary files.
        /// </summary>
        /// <param name="session"></param>
        private async ValueTask DeleteUploadAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                await ForbiddenAsync(session).ConfigureAwait(false);
                return;
            }
            if (!TryGetUploadId(session, out Guid uploadId)) {
                await ErrorAsync(session, 400, "invalid_upload_id").ConfigureAwait(false);
                return;
            }
            if (!TryRemoveUpload(uploadId, out UploadSession upload)) {
                await ErrorAsync(session, 404, "upload_not_found").ConfigureAwait(false);
                return;
            }

            upload.Cancel();
            try {
                await CleanupUploadSessionAsync(upload).ConfigureAwait(false);
                await PublishUploadCancelledAsync(upload).ConfigureAwait(false);
            }
            finally {
                upload.Dispose();
            }

            await JsonAsync(session, 200, new {
                ok = true,
                uploadId,
                cancelled = true
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Receives an entire small file into its upload session temporary file.
        /// </summary>
        /// <param name="session"></param>
        private async ValueTask UploadFileAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                await ForbiddenAsync(session).ConfigureAwait(false);
                return;
            }
            if (!TryGetUploadFile(session, out UploadSession? upload, out UploadFileState? file, out ValueTask errorResponse)) {
                await errorResponse.ConfigureAwait(false);
                return;
            }
            if (upload!.IsCancellationRequested) {
                await ErrorAsync(session, 409, "upload_cancelled").ConfigureAwait(false);
                return;
            }

            long bodyLength = session.Request.Body.Length;
            if (file!.Size > _options.UploadChunkThresholdBytes) {
                await ErrorAsync(session, 413, "use_chunk_upload").ConfigureAwait(false);
                return;
            }
            if (bodyLength != file.Size) {
                await ErrorAsync(session, 400, "invalid_file_length").ConfigureAwait(false);
                return;
            }

            if (!TryEnsureNoReparsePoints(_tempPath, out string? tempError)) {
                await ErrorAsync(session, 400, tempError).ConfigureAwait(false);
                return;
            }
            Directory.CreateDirectory(_tempPath);
            if (!TryEnsureNoReparsePoints(_tempPath, out tempError)) {
                await ErrorAsync(session, 400, tempError).ConfigureAwait(false);
                return;
            }

            bool alreadyCompleted = false;
            try {
                await file.Gate.WaitAsync(upload.Token).ConfigureAwait(false);
                try {
                    upload.Token.ThrowIfCancellationRequested();
                    if (file.Completed) {
                        alreadyCompleted = true;
                    }
                    else {
                        EnsureNoReparsePoints(file.TempPath);
                        await using FileStream fs = new(file.TempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                        await session.Request.Body.CopyToAsync(fs, upload.Token).ConfigureAwait(false);
                        file.SetSingleRange(bodyLength);
                    }
                }
                finally {
                    file.Gate.Release();
                }
            }
            catch (OperationCanceledException) when (upload.IsCancellationRequested) {
                await CleanupUploadSessionAsync(upload).ConfigureAwait(false);
                await ErrorAsync(session, 409, "upload_cancelled").ConfigureAwait(false);
                return;
            }
            if (alreadyCompleted) {
                await ErrorAsync(session, 409, "file_already_completed").ConfigureAwait(false);
                return;
            }

            upload.TryTouch(_options.UploadSessionTimeout);
            await PublishUploadProgressAsync(upload!.Id, file).ConfigureAwait(false);
            await JsonAsync(session, 200, new { ok = true, path = file.RelativePath, receivedBytes = file.ReceivedBytes, completed = file.IsComplete }).ConfigureAwait(false);
        }

        /// <summary>
        /// Receives one validated byte range for a chunked upload.
        /// </summary>
        /// <param name="session"></param>
        private async ValueTask UploadChunkAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                await ForbiddenAsync(session).ConfigureAwait(false);
                return;
            }
            if (!TryGetUploadFile(session, out UploadSession? upload, out UploadFileState? file, out ValueTask errorResponse)) {
                await errorResponse.ConfigureAwait(false);
                return;
            }
            if (upload!.IsCancellationRequested) {
                await ErrorAsync(session, 409, "upload_cancelled").ConfigureAwait(false);
                return;
            }

            if (!TryGetInt64Header(session, "X-Chunk-Offset", out long offset)) {
                await ErrorAsync(session, 400, "chunk_offset_required").ConfigureAwait(false);
                return;
            }
            long bodyLength = session.Request.Body.Length;
            if (bodyLength <= 0) {
                await ErrorAsync(session, 400, "empty_chunk").ConfigureAwait(false);
                return;
            }
            if (bodyLength > _options.UploadChunkBytes) {
                await ErrorAsync(session, 413, "chunk_too_large").ConfigureAwait(false);
                return;
            }
            if (offset < 0 || offset + bodyLength > file!.Size) {
                await ErrorAsync(session, 400, "invalid_chunk_range").ConfigureAwait(false);
                return;
            }

            if (!TryEnsureNoReparsePoints(_tempPath, out string? tempError)) {
                await ErrorAsync(session, 400, tempError).ConfigureAwait(false);
                return;
            }
            Directory.CreateDirectory(_tempPath);
            if (!TryEnsureNoReparsePoints(_tempPath, out tempError)) {
                await ErrorAsync(session, 400, tempError).ConfigureAwait(false);
                return;
            }
            bool alreadyCompleted = false;
            try {
                await file.Gate.WaitAsync(upload.Token).ConfigureAwait(false);
                try {
                    upload.Token.ThrowIfCancellationRequested();
                    if (file.Completed) {
                        alreadyCompleted = true;
                    }
                    else {
                        EnsureNoReparsePoints(file.TempPath);
                        await using FileStream fs = new(file.TempPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                        fs.Seek(offset, SeekOrigin.Begin);
                        await session.Request.Body.CopyToAsync(fs, upload.Token).ConfigureAwait(false);
                        file.AddRange(offset, bodyLength);
                    }
                }
                finally {
                    file.Gate.Release();
                }
            }
            catch (OperationCanceledException) when (upload.IsCancellationRequested) {
                await CleanupUploadSessionAsync(upload).ConfigureAwait(false);
                await ErrorAsync(session, 409, "upload_cancelled").ConfigureAwait(false);
                return;
            }
            if (alreadyCompleted) {
                await ErrorAsync(session, 409, "file_already_completed").ConfigureAwait(false);
                return;
            }

            upload.TryTouch(_options.UploadSessionTimeout);
            await PublishUploadProgressAsync(upload!.Id, file).ConfigureAwait(false);
            await JsonAsync(session, 200, new { ok = true, path = file.RelativePath, receivedBytes = file.ReceivedBytes, completed = file.IsComplete }).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates upload completeness and queues final placement of uploaded files.
        /// </summary>
        /// <param name="session"></param>
        private ValueTask CompleteUploadAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }
            if (!TryGetUploadId(session, out Guid uploadId)) {
                return ErrorAsync(session, 400, "invalid_upload_id");
            }
            if (!_uploads.TryGetValue(uploadId, out UploadSession? upload) || !upload.TryTouch(_options.UploadSessionTimeout)) {
                return ErrorAsync(session, 404, "upload_not_found");
            }
            if (upload.IsCancellationRequested) {
                return ErrorAsync(session, 409, "upload_cancelled");
            }

            foreach (UploadFileState file in upload.Files.Values) {
                file.Gate.Wait();
                try {
                    if (file.Completed) {
                        continue;
                    }
                    if (!file.IsComplete) {
                        return ErrorAsync(session, 409, "upload_incomplete");
                    }
                }
                finally {
                    file.Gate.Release();
                }
            }

            return EnqueueOperationAsync(session, "completeUpload", string.Empty, cancellationToken => {
                List<string> changedPaths = new();
                List<object> completedFiles = new();
                upload.TryTouch(_options.UploadSessionTimeout);

                foreach (UploadFileState file in upload.Files.Values) {
                    cancellationToken.ThrowIfCancellationRequested();
                    upload.Token.ThrowIfCancellationRequested();
                    file.Gate.Wait(cancellationToken);
                    try {
                        if (file.Completed) {
                            continue;
                        }
                        if (!file.IsComplete) {
                            throw new IOException("upload_incomplete");
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        upload.Token.ThrowIfCancellationRequested();
                        FinalizeFile(file);
                        file.Completed = true;
                        changedPaths.Add(ParentRelative(file.RelativePath));
                        completedFiles.Add(new {
                            uploadId,
                            path = file.RelativePath,
                            size = file.Size,
                            timestampUtc = DateTimeOffset.UtcNow
                        });
                    }
                    finally {
                        file.Gate.Release();
                    }
                }

                if (TryRemoveUpload(uploadId, out UploadSession completedUpload)) {
                    completedUpload.Dispose();
                }

                return new OperationResult(
                    ChangedPaths: changedPaths.Distinct(StringComparer.Ordinal).ToArray(),
                    Payload: new { uploadId, completed = true, files = completedFiles },
                    UploadCompletedPayloads: completedFiles.ToArray()
                );
            });
        }

        /// <summary>
        /// Resolves an upload session and file from route values and request headers.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="upload"></param>
        /// <param name="file"></param>
        /// <param name="errorResponse"></param>
        /// <returns></returns>
        private bool TryGetUploadFile(HttpSession session, out UploadSession? upload, out UploadFileState? file, out ValueTask errorResponse) {
            upload = null;
            file = null;
            errorResponse = default;

            if (!TryGetUploadId(session, out Guid uploadId)) {
                errorResponse = ErrorAsync(session, 400, "invalid_upload_id");
                return false;
            }
            if (!_uploads.TryGetValue(uploadId, out upload) || !upload.TryTouch(_options.UploadSessionTimeout)) {
                errorResponse = ErrorAsync(session, 404, "upload_not_found");
                return false;
            }
            if (!TryGetPathHeader(session, out string? rawPath)) {
                errorResponse = ErrorAsync(session, 400, "file_path_required");
                return false;
            }
            if (!TryNormalizeRelative(rawPath, allowRoot: false, out string relativePath, out string? normalizeError)) {
                errorResponse = ErrorAsync(session, 400, normalizeError);
                return false;
            }
            if (!upload.Files.TryGetValue(relativePath, out file)) {
                errorResponse = ErrorAsync(session, 404, "file_not_declared");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Reads a valid upload identifier from the current route values.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="uploadId"></param>
        /// <returns></returns>
        private bool TryGetUploadId(HttpSession session, out Guid uploadId) {
            uploadId = default;
            return session.Request.RouteValues != null
                   && session.Request.RouteValues.TryGetValue("id", out string? raw)
                   && Guid.TryParse(raw, out uploadId);
        }

        /// <summary>
        /// Reads the required upload file path, decoding the header once while preserving the already-decoded query value.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="path"></param>
        private static bool TryGetPathHeader(HttpSession session, out string? path) {
            if (session.Request.Headers.TryGetValue("X-File-Path", out path) && !string.IsNullOrWhiteSpace(path)) {
                path = DecodePathHeader(path);
                return true;
            }
            if (session.Request.Query.TryGetValue("path", out path) && !string.IsNullOrWhiteSpace(path)) {
                return true;
            }
            path = null;
            return false;
        }

        /// <summary>
        /// Reads an invariant non-negative integer request header.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="name"></param>
        /// <param name="value"></param>
        private static bool TryGetInt64Header(HttpSession session, string name, out long value) {
            value = 0;
            if (!session.Request.Headers.TryGetValue(name, out string? raw) || string.IsNullOrWhiteSpace(raw)) {
                return false;
            }
            return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Atomically promotes a completed temporary upload to its destination.
        /// </summary>
        /// <param name="file"></param>
        private void FinalizeFile(UploadFileState file) {
            string? parent = System.IO.Path.GetDirectoryName(file.TargetFullPath);
            if (!string.IsNullOrEmpty(parent)) {
                EnsureNoReparsePoints(parent);
                Directory.CreateDirectory(parent);
            }
            EnsureNoReparsePoints(file.TargetFullPath);
            if (Directory.Exists(file.TargetFullPath)) {
                throw new IOException("destination_is_directory");
            }
            EnsureNoReparsePoints(file.TempPath);
            EnsureNoReparsePoints(file.TargetFullPath);
            File.Move(file.TempPath, file.TargetFullPath, overwrite: true);
        }

        /// <summary>
        /// Adds an upload session without exceeding the configured concurrent-session limit.
        /// </summary>
        /// <param name="upload"></param>
        private bool TryAddUpload(UploadSession upload) {
            lock (_uploadsSync) {
                return _uploads.Count < _options.MaxConcurrentUploadSessions && _uploads.TryAdd(upload.Id, upload);
            }
        }

        /// <summary>
        /// Removes one upload session while synchronizing with concurrent creation.
        /// </summary>
        /// <param name="uploadId"></param>
        /// <param name="upload"></param>
        private bool TryRemoveUpload(Guid uploadId, out UploadSession upload) {
            lock (_uploadsSync) {
                return _uploads.TryRemove(uploadId, out upload!);
            }
        }

        /// <summary>
        /// Expires inactive sessions and removes old untracked upload parts.
        /// </summary>
        /// <param name="now"></param>
        private async Task CleanupExpiredUploadsAsync(DateTimeOffset now) {
            foreach (KeyValuePair<Guid, UploadSession> item in _uploads.ToArray()) {
                UploadSession upload = item.Value;
                if (upload.Files.Values.Any(static file => file.Gate.CurrentCount == 0)) {
                    continue;
                }
                if (!upload.TryExpire(now, _options.UploadSessionTimeout) || !TryRemoveUpload(item.Key, out UploadSession expiredUpload)) {
                    continue;
                }

                try {
                    await CleanupUploadSessionAsync(expiredUpload).ConfigureAwait(false);
                    await PublishUploadCancelledAsync(expiredUpload, "expired").ConfigureAwait(false);
                }
                finally {
                    expiredUpload.Dispose();
                }
            }

            CleanupAbandonedUploadFiles(now);
        }

        /// <summary>
        /// Deletes old upload parts that do not belong to a currently tracked session.
        /// </summary>
        /// <param name="now"></param>
        private void CleanupAbandonedUploadFiles(DateTimeOffset now) {
            if (!Directory.Exists(_tempPath) || !TryEnsureNoReparsePoints(_tempPath, out _)) {
                return;
            }

            HashSet<string> trackedPaths = _uploads.Values
                .SelectMany(static upload => upload.Files.Values)
                .Select(static file => file.TempPath)
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            DateTime cutoffUtc = (now - _options.UploadSessionTimeout).UtcDateTime;

            try {
                foreach (string path in Directory.EnumerateFiles(_tempPath, "*.part", SearchOption.TopDirectoryOnly)) {
                    if (trackedPaths.Contains(path)) {
                        continue;
                    }

                    try {
                        if (TryEnsureNoReparsePoints(path, out _) && File.GetLastWriteTimeUtc(path) <= cutoffUtc) {
                            File.Delete(path);
                        }
                    }
                    catch {
                        // best effort cleanup only
                    }
                }
            }
            catch {
                // best effort cleanup only
            }
        }

        /// <summary>
        /// Deletes temporary files belonging to an upload session after active writes finish.
        /// </summary>
        /// <param name="upload"></param>
        private async Task CleanupUploadSessionAsync(UploadSession upload) {
            foreach (UploadFileState file in upload.Files.Values) {
                await file.Gate.WaitAsync().ConfigureAwait(false);
                try {
                    if (TryEnsureNoReparsePoints(file.TempPath, out _)) {
                        TryDeleteFile(file.TempPath);
                    }
                }
                finally {
                    file.Gate.Release();
                }
            }
        }

        /// <summary>
        /// Performs best-effort deletion of a temporary file.
        /// </summary>
        /// <param name="path"></param>
        private static void TryDeleteFile(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch {
                // best effort cleanup only
            }
        }

        #endregion uploads

        #region authorization and paths

        /// <summary>
        /// Applies anonymous-access settings or the configured authorization callback.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        private bool IsAuthorized(HttpSession session) {
            if (_options.AllowAnonymous) {
                return true;
            }
            return _options.Authorize?.Invoke(session) == true;
        }

        /// <summary>
        /// Resolves a client path and optionally requires it to remain inside the configured root.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="allowRoot"></param>
        /// <param name="mustBeRelativeToRoot"></param>
        /// <param name="resolved"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private bool TryResolve(string? input, bool allowRoot, bool mustBeRelativeToRoot, out ResolvedPath resolved, out string? error) {
            resolved = default;
            if (!TryNormalizeRelative(input, allowRoot, out string relativePath, out error)) {
                return false;
            }

            string full = relativePath.Length == 0
                ? _options.NormalizedPath
                : System.IO.Path.Combine(_options.NormalizedPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

            full = System.IO.Path.GetFullPath(full);
            if (mustBeRelativeToRoot && !IsInsideOrEqual(full, _options.NormalizedPath)) {
                error = "path_outside_root";
                return false;
            }
            if (IsInternalPath(full)) {
                error = "internal_path_forbidden";
                return false;
            }
            if (!TryEnsureNoReparsePoints(full, out error)) {
                return false;
            }

            resolved = new ResolvedPath(relativePath, full);
            return true;
        }

        /// <summary>
        /// Normalizes a client path into a safe slash-separated relative path.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="allowRoot"></param>
        /// <param name="relativePath"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private bool TryNormalizeRelative(string? input, bool allowRoot, out string relativePath, out string? error) {
            relativePath = string.Empty;
            error = null;

            string value = (input ?? string.Empty).Trim().Replace('\\', '/');
            while (value.StartsWith("/", StringComparison.Ordinal)) {
                value = value[1..];
            }

            string[] rawSegments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (rawSegments.Length == 0) {
                if (allowRoot) {
                    return true;
                }
                error = "path_required";
                return false;
            }

            List<string> segments = new(rawSegments.Length);
            foreach (string segment in rawSegments) {
                if (segment == "." || segment == "..") {
                    error = "path_traversal_forbidden";
                    return false;
                }
                if (segment.IndexOfAny(InvalidSegmentChars) >= 0) {
                    error = "invalid_path_segment";
                    return false;
                }
                if (segments.Count == 0 && (segment.Equals(".trash", StringComparison.OrdinalIgnoreCase) || segment.Equals(TempDirectoryName, StringComparison.OrdinalIgnoreCase))) {
                    error = "internal_path_forbidden";
                    return false;
                }
                segments.Add(segment);
            }

            relativePath = string.Join("/", segments);
            return true;
        }

        /// <summary>
        /// Checks whether a full path remains inside the exposed browser root.
        /// </summary>
        /// <param name="fullPath"></param>
        /// <returns></returns>
        private bool TryEnsureInsideRoot(string fullPath) {
            string normalized = System.IO.Path.GetFullPath(fullPath);
            return IsInsideOrEqual(normalized, _options.NormalizedPath) && !IsInternalPath(normalized);
        }

        /// <summary>
        /// Rejects symbolic links, junctions and other reparse points between the browser root and a target path.
        /// Non-existing trailing segments are allowed so destinations can be created safely after a final recheck.
        /// </summary>
        /// <param name="fullPath"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private bool TryEnsureNoReparsePoints(string fullPath, out string? error) {
            return TryEnsureNoReparsePoints(fullPath, _options.NormalizedPath, out error);
        }

        /// <summary>
        /// Rejects reparse points between a trusted root and a target path.
        /// </summary>
        /// <param name="fullPath"></param>
        /// <param name="rootPath"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private bool TryEnsureNoReparsePoints(string fullPath, string rootPath, out string? error) {
            error = null;
            string root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(rootPath));
            string target = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(fullPath));
            if (!IsInsideOrEqual(target, root)) {
                error = "path_outside_root";
                return false;
            }

            string current = root;
            while (true) {
                try {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
                        error = "reparse_point_forbidden";
                        return false;
                    }
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) {
                    return true;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                    error = "path_unavailable";
                    return false;
                }

                if (string.Equals(current, target, _pathComparison)) {
                    return true;
                }

                string relative = System.IO.Path.GetRelativePath(current, target);
                int separatorIndex = relative.IndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
                string segment = separatorIndex < 0 ? relative : relative[..separatorIndex];
                current = System.IO.Path.Combine(current, segment);
            }
        }

        /// <summary>
        /// Revalidates a path immediately before a queued or direct file-system operation.
        /// </summary>
        /// <param name="fullPath"></param>
        private void EnsureNoReparsePoints(string fullPath) {
            if (!TryEnsureNoReparsePoints(fullPath, out string? error)) {
                throw new IOException(error);
            }
        }

        /// <summary>
        /// Validates a path in the configured trash, using the browser root when the trash is internal.
        /// </summary>
        /// <param name="fullPath"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private bool TryEnsureNoTrashReparsePoints(string fullPath, out string? error) {
            string trustedRoot = IsInsideOrEqual(_options.NormalizedTrashPath, _options.NormalizedPath)
                ? _options.NormalizedPath
                : _options.NormalizedTrashPath;
            return TryEnsureNoReparsePoints(fullPath, trustedRoot, out error);
        }

        /// <summary>
        /// Revalidates a configured trash path before accessing or mutating it.
        /// </summary>
        /// <param name="fullPath"></param>
        private void EnsureNoTrashReparsePoints(string fullPath) {
            if (!TryEnsureNoTrashReparsePoints(fullPath, out string? error)) {
                throw new IOException(error);
            }
        }

        /// <summary>
        /// Checks whether a path equals or is contained by a normalized root path.
        /// </summary>
        /// <param name="fullPath"></param>
        /// <param name="rootPath"></param>
        private bool IsInsideOrEqual(string fullPath, string rootPath) {
            string full = System.IO.Path.GetFullPath(fullPath);
            string root = EnsureTrailingSeparator(System.IO.Path.GetFullPath(rootPath));
            string fullTrimmed = TrimEndingDirectorySeparator(full);
            string rootTrimmed = TrimEndingDirectorySeparator(root);

            if (string.Equals(fullTrimmed, rootTrimmed, _pathComparison)) {
                return true;
            }
            return full.StartsWith(root, _pathComparison);
        }

        /// <summary>
        /// Identifies module-owned temporary and trash paths hidden from normal operations.
        /// </summary>
        /// <param name="fullPath"></param>
        private bool IsInternalPath(string fullPath) {
            string full = System.IO.Path.GetFullPath(fullPath);
            return IsInsideOrEqual(full, _options.NormalizedTrashPath) || IsInsideOrEqual(full, _tempPath);
        }

        /// <summary>
        /// Validates a single file-system entry name.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="error"></param>
        private static bool TryValidateName(string? name, out string? error) {
            error = null;
            if (string.IsNullOrWhiteSpace(name)) {
                error = "name_required";
                return false;
            }
            string trimmed = name.Trim();
            if (trimmed == "." || trimmed == ".." || trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.IndexOfAny(InvalidSegmentChars) >= 0) {
                error = "invalid_name";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Combines two relative path components with URL-style separators.
        /// </summary>
        /// <param name="parent"
        /// <param name="name"></param>
        private static string CombineRelative(string? parent, string name) {
            parent = (parent ?? string.Empty).Trim('/');
            return parent.Length == 0 ? name : parent + "/" + name;
        }

        /// <summary>
        /// Returns the parent of a relative path using URL-style separators.
        /// </summary>
        /// <param name="relativePath"></param>
        private static string ParentRelative(string relativePath) {
            int slash = relativePath.LastIndexOf('/');
            return slash < 0 ? string.Empty : relativePath[..slash];
        }

        /// <summary>
        /// Decodes percent-encoded UTF-8 data from the upload path header.
        /// </summary>
        /// <param name="value"></param>
        private static string DecodePathHeader(string value) {
            try {
                return value.IndexOf('%') >= 0 ? Uri.UnescapeDataString(value) : value;
            }
            catch (UriFormatException) {
                return value;
            }
        }

        /// <summary>
        /// Ensures a full directory path ends with a platform separator.
        /// </summary>
        /// <param name="path"></param>
        private static string EnsureTrailingSeparator(string path) {
            if (!path.EndsWith(System.IO.Path.DirectorySeparatorChar) && !path.EndsWith(System.IO.Path.AltDirectorySeparatorChar)) {
                path += System.IO.Path.DirectorySeparatorChar;
            }
            return path;
        }

        /// <summary>
        /// Removes trailing platform directory separators from a path.
        /// </summary>
        /// <param name="path"></param>
        private static string TrimEndingDirectorySeparator(string path) {
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        #endregion authorization and paths

        #region request and response helpers

        /// <summary>
        /// Deserializes a JSON request body and returns a stable client error on failure.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="session"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private static T? ReadJson<T>(HttpSession session, out string? error) where T : class {
            error = null;
            if (string.IsNullOrWhiteSpace(session.Request.BodyString)) {
                error = "json_body_required";
                return null;
            }

            try {
                return JsonSerializer.Deserialize<T>(session.Request.BodyString, JsonOptions);
            }
            catch (JsonException) {
                error = "invalid_json";
                return null;
            }
        }

        /// <summary>
        /// Sends a JSON response with the supplied status code.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="status"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static ValueTask JsonAsync(HttpSession session, int status, object value) {
            return session.Response.Status(status).Json(value).SendAsync();
        }

        /// <summary>
        /// Sends the standard file browser JSON error envelope.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="status"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        private static ValueTask ErrorAsync(HttpSession session, int status, string? error) {
            return session.Response.Status(status).Json(new { ok = false, error = error ?? "error" }).SendAsync();
        }

        /// <summary>
        /// Sends the standard forbidden response.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        private static ValueTask ForbiddenAsync(HttpSession session) {
            return ErrorAsync(session, 403, "forbidden");
        }

        #endregion request and response helpers

        #region private types

        /// <summary>
        /// Orders browser items by type and the requested sort field.
        /// </summary>
        private sealed class BrowserItemComparer : IComparer<BrowserItem> {

            private readonly string _sort;
            private readonly bool _descending;

            /// <summary>
            /// Creates a comparer for the selected sort field and direction.
            /// </summary>
            /// <param name="sort"></param>
            /// <param name="descending"></param>
            public BrowserItemComparer(string sort, bool descending) {
                _sort = sort;
                _descending = descending;
            }

            /// <summary>
            /// Compares directories before files, then applies the configured item ordering.
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <returns></returns>
            public int Compare(BrowserItem? x, BrowserItem? y) {
                if (ReferenceEquals(x, y)) {
                    return 0;
                }
                if (x == null) {
                    return -1;
                }
                if (y == null) {
                    return 1;
                }

                int result = TypeRank(x.Type).CompareTo(TypeRank(y.Type));
                if (result != 0) {
                    return result;
                }

                result = _sort switch {
                    "size" => x.Size.CompareTo(y.Size),
                    "modified" => x.ModifiedUtc.CompareTo(y.ModifiedUtc),
                    _ => StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name)
                };
                if (result != 0) {
                    return _descending ? -result : result;
                }

                result = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
                if (result != 0) {
                    return result;
                }
                result = StringComparer.Ordinal.Compare(x.Name, y.Name);
                return result != 0 ? result : StringComparer.Ordinal.Compare(x.Path, y.Path);
            }

            /// <summary>
            /// Returns the stable ordering rank associated with an item type.
            /// </summary>
            /// <param name="type"></param>
            private static int TypeRank(string type) {
                return type == "directory" ? 0 : 1;
            }

        }

        /// <summary>
        /// Captures the listing query and last item required to resume pagination.
        /// </summary>
        /// <param name="Version"></param>
        /// <param name="Path"></param>
        /// <param name="Search"></param>
        /// <param name="Sort"></param>
        /// <param name="Direction"></param>
        /// <param name="PageSize"></param>
        /// <param name="Name"></param>
        /// <param name="ItemPath"></param>
        /// <param name="Type"></param>
        /// <param name="Size"></param>
        /// <param name="ModifiedUtcTicks"></param>
        private sealed record ListContinuationToken(
            int Version,
            string Path,
            string Search,
            string Sort,
            string Direction,
            int PageSize,
            string Name,
            string ItemPath,
            string Type,
            long Size,
            long ModifiedUtcTicks
        );

        /// <summary>
        /// Describes one validated archive entry before it is extracted.
        /// </summary>
        private sealed record ArchiveEntryPlan(ZipArchiveEntry Entry, string FullPath, bool IsDirectory);

        /// <summary>
        /// Stores persistent metadata required to restore a managed trash entry.
        /// </summary>
        /// <param name="Version"></param>
        /// <param name="OriginalPath"></param>
        /// <param name="Name"></param>
        /// <param name="Type"></param>
        /// <param name="DeletedUtc"></param>
        private sealed record TrashItemMetadata(
            int Version,
            string OriginalPath,
            string Name,
            string Type,
            DateTimeOffset DeletedUtc
        );

        /// <summary>
        /// Describes a resolved managed or legacy trash entry.
        /// </summary>
        private sealed record TrashEntry(
            string Id,
            string RootPath,
            string PayloadPath,
            string Name,
            string? OriginalPath,
            string Type,
            long? Size,
            DateTimeOffset DeletedUtc,
            bool CanRestore,
            bool CanRestoreElsewhere
        );

        /// <summary>
        /// Associates a trash entry with its validated restoration destination.
        /// </summary>
        /// <param name="Entry"></param>
        /// <param name="Destination"></param>
        private sealed record TrashRestorePlan(TrashEntry Entry, ResolvedPath Destination);

        /// <summary>
        /// Holds the route, media type, content and ETag of an embedded UI asset.
        /// </summary>
        /// <param name="RouteSuffix"></param>
        /// <param name="ContentType"></param>
        /// <param name="Data"></param>
        /// <param name="ETag"></param>
        private sealed record ClientAsset(string RouteSuffix, string ContentType, byte[] Data, string ETag);

        #endregion private types

    }

}
