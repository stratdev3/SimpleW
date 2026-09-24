namespace SimpleW.Service.FileBrowser {

    internal sealed partial class FileBrowserModule {

        private const int FilesInvalidated = 1;
        private const int TrashInvalidated = 2;
        private const int AllInvalidated = FilesInvalidated | TrashInvalidated;
        private readonly string _invalidationRoom = "filebrowser:invalidation:" + Guid.NewGuid().ToString("N");
        private CancellationTokenSource? _watcherCts;
        private Task? _watcherTask;

        /// <summary>
        /// Starts optional disk monitoring independently of owner-scoped operation events.
        /// </summary>
        private void StartFileSystemWatcher() {
            if (!_options.EnableFileSystemWatcher || !_options.EnableEvents || _watcherTask != null) {
                return;
            }

            _watcherCts = new CancellationTokenSource();
            CancellationToken cancellationToken = _watcherCts.Token;
            _watcherTask = Task.Run(() => RunFileSystemWatcherAsync(cancellationToken));
        }

        /// <summary>
        /// Stops notifications and waits for all watcher resources to be released.
        /// </summary>
        private async Task StopFileSystemWatcherAsync() {
            CancellationTokenSource? cts = _watcherCts;
            Task? task = _watcherTask;
            if (cts == null) {
                return;
            }

            try {
                cts.Cancel();
                if (task != null) {
                    await task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally {
                cts.Dispose();
                _watcherCts = null;
                _watcherTask = null;
            }
        }

        /// <summary>
        /// Coalesces callbacks into bounded invalidations and recovers each failed watcher independently.
        /// </summary>
        private async Task RunFileSystemWatcherAsync(CancellationToken cancellationToken) {
            List<FileBrowserWatchState> states = [
                new(_options.NormalizedPath, trashOnly: false)
            ];
            if (!IsInsideOrEqual(_options.NormalizedTrashPath, _options.NormalizedPath)) {
                states.Add(new(_options.NormalizedTrashPath, trashOnly: true));
            }
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(300));

            try {
                do {
                    cancellationToken.ThrowIfCancellationRequested();
                    int invalidated = 0;
                    foreach (FileBrowserWatchState state in states) {
                        MaintainFileSystemWatcher(state);
                        invalidated |= Interlocked.Exchange(ref state.Invalidated, 0);
                    }
                    if (invalidated != 0 && _eventsHub != null) {
                        await _eventsHub.BroadcastTextAsync(_invalidationRoom, SerializeEvent(new {
                            files = (invalidated & FilesInvalidated) != 0,
                            trash = (invalidated & TrashInvalidated) != 0
                        }), @event: "filebrowser.invalidated").ConfigureAwait(false);
                    }
                }
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
            }
            finally {
                foreach (FileBrowserWatchState state in states) {
                    state.Watcher?.Dispose();
                }
            }
        }

        /// <summary>
        /// Drains callback errors and retries missing watchers on a five-second schedule.
        /// </summary>
        private void MaintainFileSystemWatcher(FileBrowserWatchState state) {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Exception? error = Interlocked.Exchange(ref state.Error, null);
            if (error != null) {
                _log.Warn("file browser disk monitoring reported an error", error);
            }
            if (Interlocked.Exchange(ref state.RestartRequested, 0) != 0) {
                state.Watcher?.Dispose();
                state.Watcher = null;
                state.NextCheckUtc = now.AddSeconds(5);
            }
            if (now < state.NextCheckUtc) {
                return;
            }

            try {
                ValidateWatchRoot(state);
                if (state.Watcher == null) {
                    state.Watcher = CreateFileSystemWatcher(state);
                    ValidateWatchRoot(state);
                    Interlocked.Or(ref state.Invalidated, AllInvalidated);
                }
            }
            catch (Exception exception) when (IsFileSystemWatcherFailure(exception)) {
                if (state.Watcher != null) {
                    state.Watcher.Dispose();
                    state.Watcher = null;
                    Interlocked.Or(ref state.Invalidated, AllInvalidated);
                }
                _log.Warn("file browser disk monitoring unavailable; retrying in 5 seconds", exception);
            }
            state.NextCheckUtc = now.AddSeconds(5);
        }

        /// <summary>
        /// Rechecks the watched root at startup, on recovery and during periodic health checks.
        /// </summary>
        private void ValidateWatchRoot(FileBrowserWatchState state) {
            if (state.TrashOnly) {
                EnsureNoTrashReparsePoints(state.Path);
            }
            else {
                EnsureNoReparsePoints(state.Path);
            }
            // Root removal is not reported uniformly by every platform.
            if (!Directory.Exists(state.Path)) {
                throw new DirectoryNotFoundException("A configured file browser watch directory is missing.");
            }
        }

        /// <summary>
        /// Creates a recursive watcher whose callbacks only mark pending work.
        /// </summary>
        private FileSystemWatcher CreateFileSystemWatcher(FileBrowserWatchState state) {
            FileSystemWatcher watcher = new(state.Path) {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite
            };
            try {
                watcher.Created += Changed;
                watcher.Changed += Changed;
                watcher.Deleted += Changed;
                watcher.Renamed += (_, args) => {
                    MarkPath(args.OldFullPath);
                    MarkPath(args.FullPath);
                };
                watcher.Error += (_, args) => {
                    Exception exception = args.GetException();
                    Interlocked.Exchange(ref state.Error, exception);
                    Interlocked.Or(ref state.Invalidated, AllInvalidated);
                    if (exception is not InternalBufferOverflowException) {
                        Interlocked.Exchange(ref state.RestartRequested, 1);
                    }
                };
                watcher.EnableRaisingEvents = true;
                return watcher;
            }
            catch {
                watcher.Dispose();
                throw;
            }

            void Changed(object sender, FileSystemEventArgs args) {
                MarkPath(args.FullPath);
            }

            void MarkPath(string fullPath) {
                if (IsInsideOrEqual(fullPath, _tempPath)) {
                    return;
                }
                if (IsInsideOrEqual(fullPath, _options.NormalizedTrashPath)) {
                    Interlocked.Or(ref state.Invalidated, TrashInvalidated);
                }
                else if (!state.TrashOnly && IsInsideOrEqual(fullPath, _options.NormalizedPath)) {
                    Interlocked.Or(ref state.Invalidated, FilesInvalidated);
                }
            }
        }

        /// <summary>
        /// Identifies recoverable watcher initialization and path validation failures.
        /// </summary>
        private static bool IsFileSystemWatcherFailure(Exception exception) {
            return exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
                or System.Security.SecurityException;
        }

        /// <summary>
        /// Stores a fixed amount of work regardless of the number of filesystem events.
        /// </summary>
        private sealed class FileBrowserWatchState(string path, bool trashOnly) {
            public string Path { get; } = path;
            public bool TrashOnly { get; } = trashOnly;
            public FileSystemWatcher? Watcher { get; set; }
            public DateTimeOffset NextCheckUtc { get; set; }
            public int Invalidated;
            public int RestartRequested;
            public Exception? Error;
        }

    }

}
