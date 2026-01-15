using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;


namespace SimpleW.Modules {

    /// <summary>
    /// StaticFilesModuleExtension
    /// </summary>
    public static class StaticFilesModuleExtension {

        /// <summary>
        /// Use Static Files Module
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <example>
        /// server.UseStaticFilesModule(options => {
        ///     options.Path = @"C:\www\spa\refresh\";
        ///     options.Prefix = "/";
        /// });
        /// </example>
        public static SimpleWServer UseStaticFilesModule(this SimpleWServer server, Action<StaticFilesOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            StaticFilesOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new StaticFilesModule(options));
            return server;
        }

        /// <summary>
        /// StaticFilesOptions
        /// </summary>
        public sealed class StaticFilesOptions {

            /// <summary>
            /// Path of the directory to Server
            /// </summary>
            public string Path { get; set; } = string.Empty;

            /// <summary>
            /// Url Prefix to call this module
            /// </summary>
            public string Prefix { get; set; } = "/";

            /// <summary>
            /// Cache file filter
            /// </summary>
            public string CacheFilter { get; set; } = "*";

            /// <summary>
            /// Cache timeout
            /// </summary>
            public TimeSpan? CacheTimeout { get; set; }

            /// <summary>
            /// Maximum size (in bytes) of a single file allowed to be stored in memory cache.
            /// Null means unlimited. (default 4MiB)
            /// </summary>
            public long? MaxCachedFileBytes { get; set; } = 4 * 1024 * 1024;

            /// <summary>
            /// Maximum total size (in bytes) of the in-memory file cache.
            /// Null means unlimited. (default: 256 MiB)
            /// </summary>
            public long? MaxCacheTotalBytes { get; set; } = 256 * 1024 * 1024;

            /// <summary>
            /// Maximum number of entries in the in-memory file cache.
            /// Null means unlimited. (default: 10_000)
            /// </summary>
            public int? MaxCacheEntries { get; set; } = 10_000;

            /// <summary>
            /// If true, serves a minimal directory listing when no default document exists.
            /// </summary>
            public bool AutoIndex { get; set; } = false;

            /// <summary>
            /// Default document for directory requests
            /// </summary>
            public string DefaultDocument { get; set; } = "index.html";

            /// <summary>
            /// Add a wildcard to Prefix
            /// </summary>
            public string PrefixWildCard => Prefix == "/" ? Prefix + "*" : Prefix + "/*";

            /// <summary>
            /// Check Properties and return
            /// TODO : need to check for symlink/junction
            /// </summary>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            public StaticFilesOptions ValidateAndNormalize() {

                if (string.IsNullOrWhiteSpace(Path)) {
                    throw new ArgumentException($"{nameof(StaticFilesOptions)}.{nameof(Path)} must not be null or empty.", nameof(Path));
                }
                if (string.IsNullOrWhiteSpace(Prefix)) {
                    throw new ArgumentException($"{nameof(StaticFilesOptions)}.{nameof(Prefix)} must not be null or empty.", nameof(Prefix));
                }
                if (string.IsNullOrWhiteSpace(CacheFilter)) {
                    throw new ArgumentException($"{nameof(StaticFilesOptions)}.{nameof(CacheFilter)} must not be null or empty.", nameof(CacheFilter));
                }
                if (string.IsNullOrWhiteSpace(DefaultDocument)) {
                    throw new ArgumentException($"{nameof(StaticFilesOptions)}.{nameof(DefaultDocument)} must not be null or empty.", nameof(DefaultDocument));
                }

                Path = StaticFilesModule.NormalizePath(Path);
                Prefix = SimpleWExtension.NormalizePrefix(Prefix);
                CacheTimeout = (CacheTimeout.HasValue && CacheTimeout.Value > TimeSpan.Zero) ? CacheTimeout : null;
                MaxCachedFileBytes = (MaxCachedFileBytes.HasValue && MaxCachedFileBytes.Value > 0) ? MaxCachedFileBytes : null;
                MaxCacheTotalBytes = (MaxCacheTotalBytes.HasValue && MaxCacheTotalBytes.Value > 0) ? MaxCacheTotalBytes : null;
                MaxCacheEntries = (MaxCacheEntries.HasValue && MaxCacheEntries.Value > 0) ? MaxCacheEntries : null;

                return this;
            }

        }

        /// <summary>
        /// Static Files Module
        /// </summary>
        private class StaticFilesModule : IHttpModule, IDisposable {

            /// <summary>
            /// Options
            /// </summary>
            private readonly StaticFilesOptions _options;

            /// <summary>
            /// Watcher when cache is enabled
            /// </summary>
            private FileSystemWatcher? _watcher;

            /// <summary>
            /// PathComparer
            /// </summary>
            private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

            /// <summary>
            /// Cache of file when cache is enabled
            /// </summary>
            private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(PathComparer);

            /// <summary>
            /// Current total bytes held by the file cache.
            /// </summary>
            private long _cacheBytes;

            /// <summary>
            /// Lock used to update _cacheBytes
            /// </summary>
            private readonly object _cacheEvictionLock = new();

            /// <summary>
            /// Cache HTML of auto-index (dir full path)
            /// </summary>
            private readonly ConcurrentDictionary<string, DirIndexCacheEntry> _dirIndexCache = new(PathComparer);

            /// <summary>
            /// Add static content
            /// The timeout parameter control how long the content is cached (null or 0 mean no cache at all)
            /// When cache, there is an underlying file watcher to refresh cache on file change
            /// </summary>
            /// <param name="options"></param>
            public StaticFilesModule(StaticFilesOptions options) {
                _options = options.ValidateAndNormalize() ?? throw new ArgumentNullException(nameof(options));
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("StaticFilesModule must be installed before server start.");
                }
                if (!Directory.Exists(_options.Path)) {
                    throw new DirectoryNotFoundException($"StaticFilesModule.Path '{_options.Path}' does not exist.");
                }

                // start watcher only if caching is enabled
                if (_options.CacheTimeout.HasValue) {
                    StartWatcher();
                }

                // map GET + HEAD
                server.MapGet(_options.PrefixWildCard, (HttpSession session) => HandlerAsync(session));
                server.Map("HEAD", _options.PrefixWildCard, (HttpSession session) => HandlerAsync(session));
            }

            /// <summary>
            /// Handler for Map GET/HEAD
            /// </summary>
            /// <param name="session"></param>
            /// <returns></returns>
            private async ValueTask HandlerAsync(HttpSession session) {
                HttpRequest request = session.Request;

                // resolve FS target (no disk hit here)
                if (!TryResolvePath(session, out string fullPath, out bool endsWithSlash)) {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                // HOT PATH : cache hit
                if (_options.CacheTimeout.HasValue) {
                    if (endsWithSlash) {
                        if (_cache.TryGetValue(Path.Combine(fullPath, _options.DefaultDocument), out CacheEntry entry) && !entry.IsExpired) {
                            await ServeFileAsync(session, request, entry).ConfigureAwait(false);
                            return;
                        }
                        if (_dirIndexCache.TryGetValue(fullPath, out var cached) && !cached.IsExpired) {
                            await ServeAutoIndexAsync(session, request, cached).ConfigureAwait(false);
                            return;
                        }
                    }
                    else {
                        if (_cache.TryGetValue(fullPath, out CacheEntry entry) && !entry.IsExpired) {
                            await ServeFileAsync(session, request, entry).ConfigureAwait(false);
                            return;
                        }
                    }
                }

                // directory ?
                if (Directory.Exists(fullPath)) {
                    string defaultFile = Path.Combine(fullPath, _options.DefaultDocument);
                    if (File.Exists(defaultFile)) {
                        await ServeFileAsync(session, request, defaultFile).ConfigureAwait(false);
                        return;
                    }

                    if (_options.AutoIndex) {
                        await ServeAutoIndexAsync(session, request, fullPath).ConfigureAwait(false);
                        return;
                    }

                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                // if path ends with "/" but isn't a directory -> 404
                if (endsWithSlash) {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                // file: IMPORTANT => no File.Exists here if cache is ON (ServeFileAsync will fallback to disk)
                await ServeFileAsync(session, request, fullPath).ConfigureAwait(false);
            }

            /// <summary>
            /// Dispose
            /// </summary>
            public void Dispose() {
                try { _watcher?.Dispose(); }
                catch { }
                _watcher = null;
                _cache.Clear();
                _dirIndexCache.Clear();
                lock (_cacheEvictionLock) { _cacheBytes = 0; }
            }

            /// <summary>
            /// TryResolvePath
            /// </summary>
            /// <param name="session"></param>
            /// <param name="fullPath"></param>
            /// <param name="endsWithSlash"></param>
            /// <returns></returns>
            private bool TryResolvePath(HttpSession session, out string fullPath, out bool endsWithSlash) {
                fullPath = string.Empty;
                endsWithSlash = false;

                string pathOnly = session.Request.Path;
                endsWithSlash = pathOnly.EndsWith("/", StringComparison.Ordinal);

                // remove prefix
                string relativePath = pathOnly.Length == _options.Prefix.Length ? "" : pathOnly[_options.Prefix.Length..];

                // URL decode (safe)
                try {
                    relativePath = Uri.UnescapeDataString(relativePath);
                }
                catch (UriFormatException) {
                    return false;
                }

                // normalize slashes
                relativePath = relativePath.Replace('\\', '/');
                if (relativePath.StartsWith('/')) {
                    relativePath = relativePath[1..];
                }

                // map to filesystem
                string combined = Path.Combine(_options.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
                string combinedFull = Path.GetFullPath(combined);

                // anti traversal
                StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!combinedFull.StartsWith(_options.Path, comparison)) {
                    return false;
                }

                fullPath = combinedFull;
                return true;
            }

            #region response

            /// <summary>
            /// Send a 200
            /// </summary>
            /// <param name="session"></param>
            /// <param name="contentType"></param>
            /// <param name="body"></param>
            /// <param name="lastModifiedUtc"></param>
            /// <param name="cacheSeconds"></param>
            /// <returns></returns>
            private async ValueTask SendBytesAsync(HttpSession session, string? contentType, ReadOnlyMemory<byte> body, DateTimeOffset lastModifiedUtc, int? cacheSeconds) {
                long len = body.Length;

                session.Response
                       .AddHeader("Last-Modified", lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
                       .AddHeader("Cache-Control", (cacheSeconds.HasValue && cacheSeconds.Value > 0) ? $"public, max-age={cacheSeconds.Value}" : "no-cache");

                if (session.Request.Method == "HEAD" || len == 0) {
                    if (contentType != null) {
                        session.Response.AddHeader("Content-Type", contentType);
                    }
                    await session.Response
                                 .AddHeader("Content-Length", len.ToString())
                                 .SendAsync()
                                 .ConfigureAwait(false);
                }
                else {
                    await session.Response
                                 .Body(body, contentType)
                                 .SendAsync()
                                 .ConfigureAwait(false);
                }
            }

            /// <summary>
            /// ServeFileAsync
            /// </summary>
            /// <param name="session"></param>
            /// <param name="request"></param>
            /// <param name="filePath"></param>
            /// <returns></returns>
            private async ValueTask ServeFileAsync(HttpSession session, HttpRequest request, string filePath) {

                // Cache ON (serve from cache first, fallback disk on miss)
                if (_options.CacheTimeout.HasValue) {

                    int cacheSeconds = (int)_options.CacheTimeout.Value.TotalSeconds;

                    // 1) cache hit
                    if (_cache.TryGetValue(filePath, out var entry) && !entry.IsExpired) {
                        await ServeFileAsync(session, request, entry).ConfigureAwait(false);
                        return;
                    }

                    // 2) cache miss => fallback disk (verify exists + load)
                    FileInfo fi = new(filePath);
                    if (!fi.Exists) {
                        await SendNotFoundAsync(session).ConfigureAwait(false);
                        return;
                    }

                    DateTimeOffset lastModified = fi.LastWriteTimeUtc;
                    string contentType = HttpResponse.DefaultContentType(fi.Extension);
                    string etag2 = ComputeWeakETag(fi.Length, lastModified);

                    // 304?
                    if (ShouldReturnNotModified(request, etag2, lastModified)) {
                        await SendNotModifiedAsync(session, lastModified, etag2, cacheSeconds).ConfigureAwait(false);
                        return;
                    }

                    if (_options.MaxCachedFileBytes.HasValue && fi.Length > _options.MaxCachedFileBytes.Value) {
                        // validators
                        // 304?
                        // sinon stream direct (sans cache mémoire)
                        session.Response.AddHeader("ETag", etag2);
                        await SendFileStreamAsync(session, fi, contentType, lastModified, cacheSeconds).ConfigureAwait(false);
                        return;
                    }

                    // read file into memory
                    byte[] data;
                    int len;
                    try {
                        data = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                        len = data.Length;
                    }
                    catch {
                        // if file disappears / access denied / etc.
                        await SendNotFoundAsync(session).ConfigureAwait(false);
                        return;
                    }

                    // 3) fill cache
                    TryCacheFile(filePath, data, len, contentType, etag2, lastModified);

                    // 4) send
                    session.Response.AddHeader("ETag", etag2);

                    await SendBytesAsync(
                        session,
                        contentType: contentType,
                        body: data.AsMemory(0, len),
                        lastModifiedUtc: lastModified,
                        cacheSeconds: cacheSeconds
                    ).ConfigureAwait(false);

                    return;
                }

                // Cache OFF (stream from disk)
                FileInfo fi2 = new(filePath);
                if (!fi2.Exists) {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                DateTimeOffset lastModified2 = fi2.LastWriteTimeUtc;
                string ct2 = HttpResponse.DefaultContentType(fi2.Extension);
                string etag3 = ComputeWeakETag(fi2.Length, lastModified2);

                // 304?
                if (ShouldReturnNotModified(request, etag3, lastModified2)) {
                    await SendNotModifiedAsync(session, lastModified2, etag3, cacheSeconds: null).ConfigureAwait(false);
                    return;
                }

                // 200
                session.Response.AddHeader("ETag", etag3);

                await SendFileStreamAsync(session, fi2, ct2, lastModified2, cacheSeconds: null).ConfigureAwait(false);
            }

            /// <summary>
            /// ServeFileAsync
            /// </summary>
            /// <param name="session"></param>
            /// <param name="request"></param>
            /// <param name="entry"></param>
            /// <returns></returns>
            private async ValueTask ServeFileAsync(HttpSession session, HttpRequest request, CacheEntry entry) {
                int cacheSeconds = (int)_options.CacheTimeout!.Value.TotalSeconds;

                // 304 ?
                if (ShouldReturnNotModified(request, entry.Etag, entry.LastModifiedUtc)) {
                    await SendNotModifiedAsync(session, entry.LastModifiedUtc, entry.Etag, cacheSeconds).ConfigureAwait(false);
                    return;
                }

                // 200
                session.Response.AddHeader("ETag", entry.Etag);

                await SendBytesAsync(
                    session,
                    contentType: entry.ContentType,
                    body: entry.Data.AsMemory(0, entry.Length),
                    lastModifiedUtc: entry.LastModifiedUtc,
                    cacheSeconds: cacheSeconds
                ).ConfigureAwait(false);

                return;
            }

            /// <summary>
            /// Send File Stream
            /// </summary>
            /// <param name="session"></param>
            /// <param name="fi"></param>
            /// <param name="contentType"></param>
            /// <param name="lastModifiedUtc"></param>
            /// <param name="cacheSeconds"></param>
            /// <returns></returns>
            private async ValueTask SendFileStreamAsync(HttpSession session, FileInfo fi, string contentType, DateTimeOffset lastModifiedUtc, int? cacheSeconds) {
                long length = fi.Length;

                session.Response
                       .AddHeader("Last-Modified", lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
                       .AddHeader("Cache-Control", (cacheSeconds.HasValue && cacheSeconds.Value > 0) ? $"public, max-age={cacheSeconds.Value}" : "no-cache");

                // headers only
                if (session.Request.Method == "HEAD") {
                    await session.Response
                                 .AddHeader("Content-Type", contentType)
                                 .AddHeader("Content-Length", length.ToString())
                                 .SendAsync()
                                 .ConfigureAwait(false);
                }
                // header + stream file
                else {
                    await session.Response
                                 .File(fi, contentType)
                                 .SendAsync();
                }
            }

            /// <summary>
            /// Generate AutoIndex file
            /// </summary>
            /// <param name="session"></param>
            /// <param name="request"></param>
            /// <param name="dirPath"></param>
            /// <returns></returns>
            private async ValueTask ServeAutoIndexAsync(HttpSession session, HttpRequest request, string dirPath) {

                // Cache ON ?
                if (_options.CacheTimeout.HasValue) {
                    if (_dirIndexCache.TryGetValue(dirPath, out DirIndexCacheEntry cached) && !cached.IsExpired) {
                        await ServeAutoIndexAsync(session, request, cached).ConfigureAwait(false);
                        return;
                    }
                }

                // minimal HTML listing
                StringBuilder sb = new();
                sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Index of ");
                sb.Append(HtmlEncode(request.Path));
                sb.Append("</title></head><body><h1>Index of ");
                sb.Append(HtmlEncode(request.Path));
                sb.Append("</h1><hr /><pre>");

                // parent link
                string reqPath = SimpleWExtension.NormalizePrefix(request.Path);
                bool isAtModuleRoot = (_options.Prefix == "/" && reqPath == "/")
                                      || (_options.Prefix != "/" && string.Equals(reqPath, _options.Prefix, StringComparison.Ordinal));
                if (!isAtModuleRoot) {
                    string parent = reqPath;
                    int lastSlash = parent.LastIndexOf('/');
                    if (lastSlash >= 0) {
                        parent = parent[..(lastSlash + 1)];
                        sb.Append("<a href=\"");
                        sb.Append(HtmlEncode(parent));
                        sb.Append("\">../</a><br />");
                    }
                }

                try {
                    foreach (var d in Directory.EnumerateDirectories(dirPath)) {
                        string name = Path.GetFileName(d);
                        sb.Append("<a href=\"");
                        sb.Append(HtmlEncode(EnsureTrailingSlash(JoinUrl(request.Path, name))));
                        sb.Append("\">");
                        sb.Append(HtmlEncode(name));
                        sb.Append("/</a><br />");
                    }

                    foreach (var f in Directory.EnumerateFiles(dirPath)) {
                        string name = Path.GetFileName(f);
                        sb.Append("<a href=\"");
                        sb.Append(HtmlEncode(JoinUrl(request.Path, name)));
                        sb.Append("\">");
                        sb.Append(HtmlEncode(name));
                        sb.Append("</a><br />");
                    }
                }
                catch (UnauthorizedAccessException) {
                    await SendForbiddenAsync(session).ConfigureAwait(false);
                    return;
                }
                catch {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                sb.Append("</pre><hr /></body></html>");

                byte[] html = Encoding.UTF8.GetBytes(sb.ToString());

                // store HTML in directory index cache
                if (_options.CacheTimeout.HasValue) {
                    _dirIndexCache[dirPath] = new DirIndexCacheEntry(
                        Html: html,
                        Length: html.Length,
                        ExpiresUtc: DateTimeOffset.UtcNow + _options.CacheTimeout.Value
                    );
                }

                await SendBytesAsync(
                    session,
                    contentType: "text/html; charset=utf-8",
                    body: html,
                    lastModifiedUtc: DateTimeOffset.UtcNow,
                    cacheSeconds: null
                ).ConfigureAwait(false);
            }

            /// <summary>
            /// Cached AutoIndex file
            /// </summary>
            /// <param name="session"></param>
            /// <param name="request"></param>
            /// <param name="entry"></param>
            /// <returns></returns>
            private async ValueTask ServeAutoIndexAsync(HttpSession session, HttpRequest request, DirIndexCacheEntry entry) {
                await SendBytesAsync(
                            session,
                            contentType: "text/html; charset=utf-8",
                            body: entry.Html.AsMemory(0, entry.Length),
                            lastModifiedUtc: DateTimeOffset.UtcNow,
                            cacheSeconds: null
                        ).ConfigureAwait(false);
                return;
            }

            /// <summary>
            /// Send a 404
            /// </summary>
            /// <param name="session"></param>
            /// <returns></returns>
            private async ValueTask SendNotFoundAsync(HttpSession session) {
                await session.Response
                             .Status(404)
                             .Text($"Not Found: {session.Request.Path}")
                             .SendAsync()
                             .ConfigureAwait(false);
            }

            /// <summary>
            /// Send a 403
            /// </summary>
            /// <param name="session"></param>
            /// <returns></returns>
            private async ValueTask SendForbiddenAsync(HttpSession session) {
                await session.Response
                             .Status(403)
                             .Text($"Forbidden: {session.Request.Path}")
                             .SendAsync()
                             .ConfigureAwait(false);
            }

            /// <summary>
            /// Send a 304 (must not include body)
            /// </summary>
            /// <param name="session"></param>
            /// <param name="lastModifiedUtc"></param>
            /// <param name="etag"></param>
            /// <param name="cacheSeconds"></param>
            /// <returns></returns>
            private async ValueTask SendNotModifiedAsync(HttpSession session, DateTimeOffset lastModifiedUtc, string? etag, int? cacheSeconds) {

                string cacheControl = (cacheSeconds.HasValue && cacheSeconds.Value > 0) 
                                        ? $"public, max-age={cacheSeconds.Value}"
                                        : "no-cache";

                if (!string.IsNullOrEmpty(etag)) {
                    session.Response.AddHeader("ETag", etag);
                }

                await session.Response
                       .Status(304)
                       .AddHeader("Last-Modified", lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
                       .AddHeader("Cache-Control", cacheControl)
                       .RemoveBody()
                       .SendAsync().ConfigureAwait(false);
            }

            #endregion response

            #region watcher and cache invalidation

            /// <summary>
            /// Start File System Watcher
            /// </summary>
            private void StartWatcher() {
                // best effort
                try {
                    if (!Directory.Exists(_options.Path)) {
                        return;
                    }

                    _watcher = new FileSystemWatcher(_options.Path, _options.CacheFilter) {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName
                    };

                    _watcher.Changed += OnFsChanged;
                    _watcher.Created += OnFsChanged;
                    _watcher.Deleted += OnFsChanged;
                    _watcher.Renamed += OnFsRenamed;
                    _watcher.Error += OnWatcherError;

                    _watcher.EnableRaisingEvents = true;
                }
                catch {
                    // no watcher, no cache invalidation (cache expiry still works)
                    _watcher = null;
                }
            }

            /// <summary>
            /// On FileSystem Changed
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OnFsChanged(object sender, FileSystemEventArgs e) {
                if (string.IsNullOrWhiteSpace(e.FullPath)) {
                    return;
                }

                if (_cache.TryRemove(e.FullPath, out var removed)) {
                    lock (_cacheEvictionLock) {
                        _cacheBytes -= removed.Length;
                    }
                }

                // invalidate directory index cache for the parent directory
                string? parent = Path.GetDirectoryName(e.FullPath);
                if (!string.IsNullOrEmpty(parent)) {
                    _dirIndexCache.TryRemove(parent, out _);
                }
            }

            /// <summary>
            /// On FileSystem Renamed
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            private void OnFsRenamed(object sender, RenamedEventArgs e) {
                if (!string.IsNullOrWhiteSpace(e.OldFullPath)) {
                    if (_cache.TryRemove(e.OldFullPath, out var oldRemoved)) {
                        lock (_cacheEvictionLock) {
                            _cacheBytes -= oldRemoved.Length;
                        }
                    }

                    string? oldParent = Path.GetDirectoryName(e.OldFullPath);
                    if (!string.IsNullOrEmpty(oldParent)) {
                        _dirIndexCache.TryRemove(oldParent, out _);
                    }
                }

                if (!string.IsNullOrWhiteSpace(e.FullPath)) {
                    if (_cache.TryRemove(e.FullPath, out var newRemoved)) {
                        lock (_cacheEvictionLock) {
                            _cacheBytes -= newRemoved.Length;
                        }
                    }
                    string? newParent = Path.GetDirectoryName(e.FullPath);
                    if (!string.IsNullOrEmpty(newParent)) {
                        _dirIndexCache.TryRemove(newParent, out _);
                    }
                }
            }

            /// <summary>
            /// On Watcher Error
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="e"></param>
            /// <exception cref="NotImplementedException"></exception>
            private void OnWatcherError(object sender, ErrorEventArgs e) {
                _cache.Clear();
                _dirIndexCache.Clear();
                lock (_cacheEvictionLock) { _cacheBytes = 0; }
            }

            /// <summary>
            /// Cache Entry
            /// </summary>
            /// <param name="Data"></param>
            /// <param name="Length"></param>
            /// <param name="ContentType"></param>
            /// <param name="LastModifiedUtc"></param>
            /// <param name="ExpiresUtc"></param>
            private readonly record struct CacheEntry(byte[] Data, int Length, string ContentType, string Etag, DateTimeOffset LastModifiedUtc, DateTimeOffset ExpiresUtc) {
                public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresUtc;
            }

            /// <summary>
            /// DirIndex Cache Entry
            /// </summary>
            /// <param name="Html"></param>
            /// <param name="Length"></param>
            /// <param name="ExpiresUtc"></param>
            private readonly record struct DirIndexCacheEntry(byte[] Html, int Length, DateTimeOffset ExpiresUtc) {
                public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresUtc;
            }

            #endregion watcher and cache invalidation

            #region helpers

            /// <summary>
            /// normalize path, GetFullPath and add trailing slash to avoid path traversal
            /// </summary>
            /// <param name="path"></param>
            /// <returns></returns>
            public static string NormalizePath(string path) {
                path = Path.GetFullPath(path);
                if (!path.EndsWith(Path.DirectorySeparatorChar) && !path.EndsWith(Path.AltDirectorySeparatorChar)) {
                    path += Path.DirectorySeparatorChar;
                }
                return path;
            }

            /// <summary>
            /// Try to cache an entry while enforcing cache limits (max file size / max total size / max entries).
            /// Best-effort eviction (expired first, then arbitrary) to make room.
            /// </summary>
            /// <param name="filePath"></param>
            /// <param name="data"></param>
            /// <param name="len"></param>
            /// <param name="contentType"></param>
            /// <param name="etag"></param>
            /// <param name="lastModifiedUtc"></param>
            /// <returns></returns>
            private bool TryCacheFile(string filePath, byte[] data, int len, string contentType, string etag, DateTimeOffset lastModifiedUtc) {

                // enforce MaxCachedFileBytes
                if (_options.MaxCachedFileBytes.HasValue && len > _options.MaxCachedFileBytes.Value) {
                    return false;
                }
                // if a single item is larger than total limit, never cache it
                if (_options.MaxCacheTotalBytes.HasValue && len > _options.MaxCacheTotalBytes.Value) {
                    return false;
                }

                DateTimeOffset expires = DateTimeOffset.UtcNow + _options.CacheTimeout!.Value;

                lock (_cacheEvictionLock) {

                    EvictExpiredLocked();

                    // enforce MaxCacheEntries
                    if (_options.MaxCacheEntries.HasValue) {
                        while (_cache.Count >= _options.MaxCacheEntries.Value) {
                            if (!EvictOneLocked(exceptKey: filePath))
                                return false;
                        }
                    }

                    // enforce MaxCacheTotalBytes before adding cache
                    if (_options.MaxCacheTotalBytes.HasValue) {
                        while (_cacheBytes + len > _options.MaxCacheTotalBytes.Value) {
                            if (!EvictOneLocked(exceptKey: filePath)) {
                                return false;
                            }
                        }
                    }

                    _cache.AddOrUpdate(
                        filePath,
                        addValueFactory: _ => {
                            _cacheBytes += len;
                            return new CacheEntry(data, len, contentType, etag, lastModifiedUtc, expires);
                        },
                        updateValueFactory: (_, old) => {
                            _cacheBytes += (len - old.Length);
                            return new CacheEntry(data, len, contentType, etag, lastModifiedUtc, expires);
                        }
                    );

                    // enforce MaxCacheTotalBytes after added cache
                    if (_options.MaxCacheTotalBytes.HasValue) {
                        while (_cacheBytes > _options.MaxCacheTotalBytes.Value) {
                            if (!EvictOneLocked(exceptKey: filePath)) {
                                break;
                            }
                        }
                    }
                }

                return true;
            }

            /// <summary>
            /// EvictExpiredLocked
            /// </summary>
            private void EvictExpiredLocked() {
                foreach (var kv in _cache) {
                    if (kv.Value.IsExpired) {
                        if (_cache.TryRemove(kv.Key, out var removed)) {
                            _cacheBytes -= removed.Length;
                        }
                    }
                }
            }

            /// <summary>
            /// EvictOneLocked
            /// </summary>
            /// <param name="exceptKey"></param>
            /// <returns></returns>
            private bool EvictOneLocked(string? exceptKey) {

                // expired entries first
                foreach (var kv in _cache) {
                    if (kv.Key == exceptKey) {
                        continue;
                    }
                    if (kv.Value.IsExpired) {
                        if (_cache.TryRemove(kv.Key, out var removed)) {
                            _cacheBytes -= removed.Length;
                            return true;
                        }
                    }
                }

                // else evict an arbitrary entry (best-effort)
                foreach (var kv in _cache) {
                    if (kv.Key == exceptKey) {
                        continue;
                    }
                    if (_cache.TryRemove(kv.Key, out var removed)) {
                        _cacheBytes -= removed.Length;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// TryGetIfModifiedSince
            /// </summary>
            /// <param name="request"></param>
            /// <param name="imsUtc"></param>
            /// <returns></returns>
            private static bool TryGetIfModifiedSince(HttpRequest request, out DateTimeOffset imsUtc) {
                imsUtc = default;
                if (!request.Headers.TryGetValue("If-Modified-Since", out var v) || string.IsNullOrWhiteSpace(v)) {
                    return false;
                }

                // RFC1123 generally
                if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)) {
                    imsUtc = dto;
                    return true;
                }
                return false;
            }

            /// <summary>
            /// ComputeWeakETag
            /// </summary>
            /// <param name="length"></param>
            /// <param name="lastModifiedUtc"></param>
            /// <returns></returns>
            private static string ComputeWeakETag(long length, DateTimeOffset lastModifiedUtc) {
                // Weak ETag based on length + last write ticks (UTC)
                // Example: W/"12345-638420123456789012"
                return $"W/\"{length}-{lastModifiedUtc.UtcTicks}\"";
            }

            /// <summary>
            /// IfNoneMatchHasMatch
            /// </summary>
            /// <param name="request"></param>
            /// <param name="currentEtag"></param>
            /// <returns></returns>
            private static bool IfNoneMatchHasMatch(HttpRequest request, string currentEtag) {

                if (!request.Headers.TryGetValue("If-None-Match", out string? inm) || string.IsNullOrWhiteSpace(inm)) {
                    return false;
                }

                inm = inm.Trim();

                // "*" means "any current representation"
                if (inm == "*")
                    return true;

                foreach (var raw in inm.Split(',')) {
                    var token = raw.Trim();
                    if (token.Length == 0)
                        continue;

                    // Exact match
                    if (string.Equals(token, currentEtag, StringComparison.Ordinal))
                        return true;

                    // Accept strong/weak equivalent: W/"x" <-> "x"
                    if (NormalizeEtag(token) == NormalizeEtag(currentEtag))
                        return true;
                }

                return false;

                static string NormalizeEtag(string etag) {
                    etag = etag.Trim();
                    if (etag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) {
                        etag = etag.Substring(2).TrimStart();
                    }
                    return etag;
                }
            }

            /// <summary>
            /// ShouldReturnNotModified
            /// </summary>
            /// <param name="request"></param>
            /// <param name="etag"></param>
            /// <param name="lastModifiedUtc"></param>
            /// <returns></returns>
            private static bool ShouldReturnNotModified(HttpRequest request, string etag, DateTimeOffset lastModifiedUtc) {

                // 1) If-None-Match first (priority)
                if (IfNoneMatchHasMatch(request, etag)) {
                    return true;
                }

                // 2) Fallback If-Modified-Since
                if (TryGetIfModifiedSince(request, out DateTimeOffset ims) && ims >= TruncateToSeconds(lastModifiedUtc)) {
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Truncate DateTimeOffset To Seconds
            /// </summary>
            /// <param name="dt"></param>
            /// <returns></returns>
            private static DateTimeOffset TruncateToSeconds(DateTimeOffset dt) => dt.AddTicks(-(dt.Ticks % TimeSpan.TicksPerSecond));

            /// <summary>
            /// EnsureTrailingSlash
            /// </summary>
            /// <param name="p"></param>
            /// <returns></returns>
            private static string EnsureTrailingSlash(string p) => p.EndsWith('/') ? p : p + "/";

            /// <summary>
            /// JoinUrl
            /// </summary>
            /// <param name="basePath"></param>
            /// <param name="segment"></param>
            /// <returns></returns>
            private static string JoinUrl(string basePath, string segment) {
                if (string.IsNullOrEmpty(basePath)) {
                    return "/" + segment;
                }
                if (!basePath.EndsWith('/')) {
                    basePath += "/";
                }
                return basePath + Uri.EscapeDataString(segment);
            }

            /// <summary>
            /// HtmlEncode
            /// </summary>
            /// <param name="s"></param>
            /// <returns></returns>
            private static string HtmlEncode(string s) {
                if (string.IsNullOrEmpty(s)) {
                    return string.Empty;
                }
                return WebUtility.HtmlEncode(s);
            }

            #endregion helpers

        }

    }

}
