using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using SimpleW.Modules;
using SimpleW.Observability;

namespace SimpleW.Service.FileBrowser {

    internal sealed class FileBrowserModule : IHttpModule {

        private const string TempDirectoryName = ".filebrowser-tmp";
        private const string EventsRoom = "filebrowser";
        private static readonly ILogger _log = new Logger<FileBrowserModule>();
        private static readonly Lazy<ClientAsset[]> EmbeddedClientAssets = new(LoadEmbeddedClientAssets);
        private static readonly JsonSerializerOptions JsonOptions = new() {
            PropertyNameCaseInsensitive = true
        };
        private static readonly char[] InvalidSegmentChars = System.IO.Path.GetInvalidFileNameChars();

        private readonly FileBrowserOptions _options;
        private readonly ConcurrentDictionary<Guid, UploadSession> _uploads = new();
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

        public FileBrowserModule(FileBrowserOptions options) {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
            _tempPath = System.IO.Path.Combine(_options.NormalizedPath, TempDirectoryName);
        }

        public void Install(SimpleWServer server) {

            Directory.CreateDirectory(_options.NormalizedPath);
            Directory.CreateDirectory(_options.NormalizedTrashPath);
            Directory.CreateDirectory(_tempPath);

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
            server.Map("POST", Route("/api/folders"), (HttpSession session) => CreateFolderAsync(session));
            server.Map("POST", Route("/api/rename"), (HttpSession session) => RenameAsync(session));
            server.Map("POST", Route("/api/move"), (HttpSession session) => MoveAsync(session));
            server.Map("POST", Route("/api/delete"), (HttpSession session) => DeleteAsync(session));
            server.Map("POST", Route("/api/operations/cancel"), (HttpSession session) => CancelOperationsAsync(session));
            server.Map("POST", Route("/api/uploads"), (HttpSession session) => CreateUploadAsync(session));
            server.Map("POST", Route("/api/uploads/:id/files"), (HttpSession session) => UploadFileAsync(session));
            server.Map("POST", Route("/api/uploads/:id/chunks"), (HttpSession session) => UploadChunkAsync(session));
            server.Map("POST", Route("/api/uploads/:id/complete"), (HttpSession session) => CompleteUploadAsync(session));

            _log.Info($"installed with prefix {_options.NormalizedPrefix}");
        }

        private string Route(string suffix) {
            if (_options.NormalizedPrefix == "/") {
                return suffix.StartsWith("/", StringComparison.Ordinal) ? suffix : "/" + suffix;
            }
            return _options.NormalizedPrefix + (suffix.StartsWith("/", StringComparison.Ordinal) ? suffix : "/" + suffix);
        }

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

        private static bool IsClientDirectory(string path) {
            return Directory.Exists(path)
                && File.Exists(System.IO.Path.Combine(path, "index.html"))
                && File.Exists(System.IO.Path.Combine(path, "app.js"))
                && File.Exists(System.IO.Path.Combine(path, "styles.css"));
        }

        private static string NormalizeDirectory(string path) {
            string full = System.IO.Path.GetFullPath(path);
            if (!full.EndsWith(System.IO.Path.DirectorySeparatorChar) && !full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)) {
                full += System.IO.Path.DirectorySeparatorChar;
            }
            return full;
        }

        private void InstallEmbeddedClient(SimpleWServer server) {
            foreach (ClientAsset asset in EmbeddedClientAssets.Value) {
                string route = Route(asset.RouteSuffix);
                server.MapGet(route, (HttpSession session) => EmbeddedClientAssetAsync(session, asset));
                server.Map("HEAD", route, (HttpSession session) => EmbeddedClientAssetAsync(session, asset));
            }
        }

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

        private static ClientAsset[] LoadEmbeddedClientAssets() {
            return [
                LoadEmbeddedClientAsset("/", "index.html", "text/html"),
                LoadEmbeddedClientAsset("/app.js", "app.js", "text/javascript"),
                LoadEmbeddedClientAsset("/styles.css", "styles.css", "text/css")
            ];
        }

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

        private void StartOperations() {
            if (_operationsTask != null) {
                return;
            }

            _operationsCts = new CancellationTokenSource();
            _operationsTask = Task.Run(() => RunOperationsAsync(_operationsCts.Token));
        }

        private async Task StopOperationsAsync() {
            CancellationTokenSource? cts = _operationsCts;
            Task? task = _operationsTask;
            _operationsCts = null;
            _operationsTask = null;

            if (cts == null || task == null) {
                return;
            }

            try {
                foreach (QueuedOperation operation in _trackedOperations.Values) {
                    operation.Cancel();
                }
                foreach (UploadSession upload in _uploads.Values) {
                    upload.Cancel();
                    CleanupUploadSession(upload);
                }
                cts.Cancel();
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally {
                cts.Dispose();
            }
        }

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

        private ValueTask PublishOperationCancelledAsync(QueuedOperation operation) {
            return PublishEventAsync("filebrowser.operation.cancelled", new {
                operationId = operation.Id,
                operation = operation.Kind,
                path = operation.Path,
                parentPath = ParentRelative(operation.Path),
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

        private ValueTask PublishChangedAsync(Guid operationId, string operation, string path) {
            return PublishEventAsync("filebrowser.changed", new {
                operationId,
                operation,
                path,
                parentPath = ParentRelative(path),
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

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

        private async ValueTask PublishEventAsync(string eventName, object payload) {
            if (!_options.EnableEvents || _eventsHub == null) {
                return;
            }

            await _eventsHub.BroadcastTextAsync(EventsRoom, SerializeEvent(payload), @event: eventName).ConfigureAwait(false);
        }

        private static string SerializeEvent(object payload) {
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

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
                maxFileBytes = _options.MaxFileBytes,
                maxUploadBytes = _options.MaxUploadBytes
            });
        }

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

            List<BrowserItem> items = new();
            foreach (string directory in Directory.EnumerateDirectories(resolved.FullPath)) {
                if (IsInternalPath(directory)) {
                    continue;
                }
                DirectoryInfo info = new(directory);
                items.Add(new BrowserItem(info.Name, CombineRelative(resolved.RelativePath, info.Name), "directory", 0, info.LastWriteTimeUtc));
            }
            foreach (string file in Directory.EnumerateFiles(resolved.FullPath)) {
                if (IsInternalPath(file)) {
                    continue;
                }
                FileInfo info = new(file);
                items.Add(new BrowserItem(info.Name, CombineRelative(resolved.RelativePath, info.Name), "file", info.Length, info.LastWriteTimeUtc));
            }

            return JsonAsync(session, 200, new {
                ok = true,
                path = resolved.RelativePath,
                parent = ParentRelative(resolved.RelativePath),
                items = items.OrderBy(static i => i.Type, StringComparer.Ordinal)
                             .ThenBy(static i => i.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(static i => new {
                                 name = i.Name,
                                 path = i.Path,
                                 type = i.Type,
                                 size = i.Size,
                                 modifiedUtc = i.ModifiedUtc
                             })
                             .ToArray()
            });
        }

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
                Directory.CreateDirectory(resolved.FullPath);
                return new OperationResult(
                    ChangedPaths: [ParentRelative(resolved.RelativePath)],
                    Payload: new { path = resolved.RelativePath }
                );
            });
        }

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
                    File.Move(source.FullPath, destinationFull);
                }
                else if (Directory.Exists(source.FullPath)) {
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
                    Directory.Move(source.FullPath, destinationFull);
                }
                else {
                    File.Move(source.FullPath, destinationFull);
                }

                return new OperationResult(
                    ChangedPaths: [ParentRelative(source.RelativePath), destinationDirectory.RelativePath],
                    Payload: new { sourcePath = source.RelativePath, destinationPath = destinationRelative }
                );
            });
        }

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

            Directory.CreateDirectory(_options.NormalizedTrashPath);
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
                    if (!File.Exists(source.FullPath) && !Directory.Exists(source.FullPath)) {
                        throw new FileNotFoundException("source_not_found", source.FullPath);
                    }

                    string trashFull = CreateUniqueTrashPath(source.RelativePath);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.Exists(source.FullPath)) {
                        File.Move(source.FullPath, trashFull);
                    }
                    else {
                        Directory.Move(source.FullPath, trashFull);
                    }
                    deleted.Add(new { path = source.RelativePath });
                    changedPaths.Add(ParentRelative(source.RelativePath));
                }

                return new OperationResult(
                    ChangedPaths: changedPaths.Distinct(StringComparer.Ordinal).ToArray(),
                    Payload: new { deleted }
                );
            });
        }

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
                UploadSession upload = item.Value;
                if (upload.Cancel()) {
                    cancelledUploads++;
                    CleanupUploadSession(upload);
                    _uploads.TryRemove(item.Key, out _);
                    await PublishUploadCancelledAsync(upload).ConfigureAwait(false);
                }
            }

            await JsonAsync(session, 202, new {
                ok = true,
                cancelledOperations,
                cancelledUploads
            }).ConfigureAwait(false);
        }

        private ValueTask PublishUploadCancelledAsync(UploadSession upload) {
            return PublishEventAsync("filebrowser.upload.cancelled", new {
                uploadId = upload.Id,
                files = upload.Files.Keys.ToArray(),
                timestampUtc = DateTimeOffset.UtcNow
            });
        }

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
            _uploads[uploadId] = upload;

            return JsonAsync(session, 201, new {
                ok = true,
                uploadId,
                chunkThresholdBytes = _options.UploadChunkThresholdBytes,
                chunkBytes = _options.UploadChunkBytes
            });
        }

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

            Directory.CreateDirectory(_tempPath);
            string? parent = System.IO.Path.GetDirectoryName(file.TargetFullPath);
            if (!string.IsNullOrEmpty(parent)) {
                Directory.CreateDirectory(parent);
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
                CleanupUploadSession(upload);
                await ErrorAsync(session, 409, "upload_cancelled").ConfigureAwait(false);
                return;
            }
            if (alreadyCompleted) {
                await ErrorAsync(session, 409, "file_already_completed").ConfigureAwait(false);
                return;
            }

            await PublishUploadProgressAsync(upload!.Id, file).ConfigureAwait(false);
            await JsonAsync(session, 200, new { ok = true, path = file.RelativePath, receivedBytes = file.ReceivedBytes, completed = file.IsComplete }).ConfigureAwait(false);
        }

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

            Directory.CreateDirectory(_tempPath);
            bool alreadyCompleted = false;
            try {
                await file.Gate.WaitAsync(upload.Token).ConfigureAwait(false);
                try {
                    upload.Token.ThrowIfCancellationRequested();
                    if (file.Completed) {
                        alreadyCompleted = true;
                    }
                    else {
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
                CleanupUploadSession(upload);
                await ErrorAsync(session, 409, "upload_cancelled").ConfigureAwait(false);
                return;
            }
            if (alreadyCompleted) {
                await ErrorAsync(session, 409, "file_already_completed").ConfigureAwait(false);
                return;
            }

            await PublishUploadProgressAsync(upload!.Id, file).ConfigureAwait(false);
            await JsonAsync(session, 200, new { ok = true, path = file.RelativePath, receivedBytes = file.ReceivedBytes, completed = file.IsComplete }).ConfigureAwait(false);
        }

        private ValueTask CompleteUploadAsync(HttpSession session) {
            if (!IsAuthorized(session)) {
                return ForbiddenAsync(session);
            }
            if (!TryGetUploadId(session, out Guid uploadId)) {
                return ErrorAsync(session, 400, "invalid_upload_id");
            }
            if (!_uploads.TryGetValue(uploadId, out UploadSession? upload)) {
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

                _uploads.TryRemove(uploadId, out _);

                return new OperationResult(
                    ChangedPaths: changedPaths.Distinct(StringComparer.Ordinal).ToArray(),
                    Payload: new { uploadId, completed = true, files = completedFiles },
                    UploadCompletedPayloads: completedFiles.ToArray()
                );
            });
        }

        private bool TryGetUploadFile(HttpSession session, out UploadSession? upload, out UploadFileState? file, out ValueTask errorResponse) {
            upload = null;
            file = null;
            errorResponse = default;

            if (!TryGetUploadId(session, out Guid uploadId)) {
                errorResponse = ErrorAsync(session, 400, "invalid_upload_id");
                return false;
            }
            if (!_uploads.TryGetValue(uploadId, out upload)) {
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

        private bool TryGetUploadId(HttpSession session, out Guid uploadId) {
            uploadId = default;
            return session.Request.RouteValues != null
                   && session.Request.RouteValues.TryGetValue("id", out string? raw)
                   && Guid.TryParse(raw, out uploadId);
        }

        private static bool TryGetPathHeader(HttpSession session, out string? path) {
            if (session.Request.Headers.TryGetValue("X-File-Path", out path) && !string.IsNullOrWhiteSpace(path)) {
                return true;
            }
            if (session.Request.Query.TryGetValue("path", out path) && !string.IsNullOrWhiteSpace(path)) {
                return true;
            }
            path = null;
            return false;
        }

        private static bool TryGetInt64Header(HttpSession session, string name, out long value) {
            value = 0;
            if (!session.Request.Headers.TryGetValue(name, out string? raw) || string.IsNullOrWhiteSpace(raw)) {
                return false;
            }
            return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private void FinalizeFile(UploadFileState file) {
            string? parent = System.IO.Path.GetDirectoryName(file.TargetFullPath);
            if (!string.IsNullOrEmpty(parent)) {
                Directory.CreateDirectory(parent);
            }
            if (Directory.Exists(file.TargetFullPath)) {
                throw new IOException("destination_is_directory");
            }
            File.Move(file.TempPath, file.TargetFullPath, overwrite: true);
        }

        private static void CleanupUploadSession(UploadSession upload) {
            foreach (UploadFileState file in upload.Files.Values) {
                if (!file.Gate.Wait(0)) {
                    continue;
                }
                try {
                    TryDeleteFile(file.TempPath);
                }
                finally {
                    file.Gate.Release();
                }
            }
        }

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

        private bool IsAuthorized(HttpSession session) {
            if (_options.AllowAnonymous) {
                return true;
            }
            return _options.Authorize?.Invoke(session) == true;
        }

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

            resolved = new ResolvedPath(relativePath, full);
            return true;
        }

        private bool TryNormalizeRelative(string? input, bool allowRoot, out string relativePath, out string? error) {
            relativePath = string.Empty;
            error = null;

            string value = UrlDecode(input ?? string.Empty).Trim().Replace('\\', '/');
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

        private bool TryEnsureInsideRoot(string fullPath) {
            string normalized = System.IO.Path.GetFullPath(fullPath);
            return IsInsideOrEqual(normalized, _options.NormalizedPath) && !IsInternalPath(normalized);
        }

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

        private bool IsInternalPath(string fullPath) {
            string full = System.IO.Path.GetFullPath(fullPath);
            return IsInsideOrEqual(full, _options.NormalizedTrashPath) || IsInsideOrEqual(full, _tempPath);
        }

        private string CreateUniqueTrashPath(string relativePath) {
            string leaf = SanitizeTrashName(relativePath);
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            string candidate = System.IO.Path.Combine(_options.NormalizedTrashPath, $"{timestamp}-{leaf}");
            int i = 1;
            while (File.Exists(candidate) || Directory.Exists(candidate)) {
                candidate = System.IO.Path.Combine(_options.NormalizedTrashPath, $"{timestamp}-{i.ToString(CultureInfo.InvariantCulture)}-{leaf}");
                i++;
            }
            return candidate;
        }

        private static string SanitizeTrashName(string relativePath) {
            string name = relativePath.Replace('/', '_').Replace('\\', '_').Trim('_');
            if (string.IsNullOrWhiteSpace(name)) {
                return "item";
            }
            foreach (char c in InvalidSegmentChars) {
                name = name.Replace(c, '_');
            }
            return name;
        }

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

        private static string CombineRelative(string? parent, string name) {
            parent = (parent ?? string.Empty).Trim('/');
            return parent.Length == 0 ? name : parent + "/" + name;
        }

        private static string ParentRelative(string relativePath) {
            int slash = relativePath.LastIndexOf('/');
            return slash < 0 ? string.Empty : relativePath[..slash];
        }

        private static string UrlDecode(string value) {
            try {
                return value.IndexOf('%') >= 0 ? Uri.UnescapeDataString(value) : value;
            }
            catch (UriFormatException) {
                return value;
            }
        }

        private static string EnsureTrailingSeparator(string path) {
            if (!path.EndsWith(System.IO.Path.DirectorySeparatorChar) && !path.EndsWith(System.IO.Path.AltDirectorySeparatorChar)) {
                path += System.IO.Path.DirectorySeparatorChar;
            }
            return path;
        }

        private static string TrimEndingDirectorySeparator(string path) {
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

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

        private static ValueTask JsonAsync(HttpSession session, int status, object value) {
            return session.Response.Status(status).Json(value).SendAsync();
        }

        private static ValueTask ErrorAsync(HttpSession session, int status, string? error) {
            return session.Response.Status(status).Json(new { ok = false, error = error ?? "error" }).SendAsync();
        }

        private static ValueTask ForbiddenAsync(HttpSession session) {
            return ErrorAsync(session, 403, "forbidden");
        }

        private sealed record ClientAsset(string RouteSuffix, string ContentType, byte[] Data, string ETag);

    }

}
