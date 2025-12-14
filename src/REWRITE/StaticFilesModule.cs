using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;


namespace SimpleW {

    /// <summary>
    /// StaticFilesModule
    /// </summary>
    public class StaticFilesModule : IHttpModule, IDisposable {

        /// <summary>
        /// Path of the directory to Server
        /// </summary>
        private readonly string _rootpath;

        /// <summary>
        /// Full _rootpath
        /// </summary>
        private readonly string _rootPathFull;

        /// <summary>
        /// Url Prefix to call this module
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        /// Cache file filter
        /// </summary>
        private readonly string _filter;

        /// <summary>
        /// Cache timeout
        /// </summary>
        private readonly TimeSpan? _cacheTimeout;

        /// <summary>
        /// Watcher when cache is enabled
        /// </summary>
        private FileSystemWatcher? _watcher;

        /// <summary>
        /// Cache of file when cache is enabled
        /// </summary>
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

        /// <summary>
        /// Cache HTML of auto-index (dir full path)
        /// </summary>
        private readonly ConcurrentDictionary<string, DirIndexCacheEntry> _dirIndexCache = new(StringComparer.Ordinal);

        /// <summary>
        /// used for streaming
        /// </summary>
        private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

        /// <summary>
        /// Default document for directory requests
        /// </summary>
        public string DefaultDocument { get; set; } = "index.html";

        /// <summary>
        /// If true, serves a minimal directory listing when no default document exists.
        /// </summary>
        public bool AutoIndex { get; set; } = false;

        /// <summary>
        /// If true, unknown extensions default to application/octet-stream (otherwise text/plain).
        /// </summary>
        public bool UnknownContentTypeAsOctetStream { get; set; } = true;

        /// <summary>
        /// Add static content
        /// The timeout parameter control how long the content is cached (null or 0 mean no cache at all)
        /// When cache, there is an underlying file watcher to refresh cache on file change
        /// </summary>
        /// <param name="path">Static content path</param>
        /// <param name="prefix">Cache prefix (default is "/")</param>
        /// <param name="filter">Cache filter (default is "*.*")</param>
        /// <param name="timeout">Refresh cache timeout (0 or null mean no cache, default: null)</param>
        public StaticFilesModule(string path, string prefix = "/", string filter = "*", TimeSpan? timeout = null) {
            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentException("path must not be null or empty.", nameof(path));
            }
            if (string.IsNullOrWhiteSpace(prefix)) {
                throw new ArgumentException("prefix must not be null or empty.", nameof(prefix));
            }

            _rootpath = path;
            _rootPathFull = Path.GetFullPath(path);

            _prefix = NormalizePrefix(prefix);
            _filter = string.IsNullOrWhiteSpace(filter) ? "*.*" : filter;
            _cacheTimeout = (timeout.HasValue && timeout.Value > TimeSpan.Zero) ? timeout : null;
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        public void Install(SimpleW server) {
            if (server.IsStarted) {
                throw new InvalidOperationException("StaticFilesModule must be installed before server start.");
            }

            // start watcher only if caching is enabled
            if (_cacheTimeout.HasValue) {
                StartWatcher();
            }

            server.UseMiddleware(HandleAsync);
        }

        /// <summary>
        /// Middleware
        /// </summary>
        /// <param name="session"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        private async ValueTask HandleAsync(HttpSession session, Func<ValueTask> next) {
            HttpRequest request = session.Request;

            // only GET/HEAD
            if (request.Method != "GET" && request.Method != "HEAD") {
                await next().ConfigureAwait(false);
                return;
            }

            // only this prefix
            if (!request.Path.StartsWith(_prefix)) {
                await next().ConfigureAwait(false);
                return;
            }

            // resolve FS target (no disk hit here)
            if (!TryResolvePath(session, out string fullPath, out bool endsWithSlash)) {
                await SendNotFoundAsync(session).ConfigureAwait(false);
                return;
            }

            // if cache is ON and we already cached an auto-index for this directory
            if (_cacheTimeout.HasValue && AutoIndex) {
                if (_dirIndexCache.TryGetValue(fullPath, out var dirCached) && !dirCached.IsExpired) {
                    // ServeAutoIndexAsync will immediately hit the cache and return.
                    await ServeAutoIndexAsync(session, request, fullPath).ConfigureAwait(false);
                    return;
                }
            }

            // fast path if URL ends with "/" (directory intent) and we have a cached
            if (_cacheTimeout.HasValue && AutoIndex && endsWithSlash) {
                if (_dirIndexCache.TryGetValue(fullPath, out var cachedDir) && !cachedDir.IsExpired) {
                    // ServeAutoIndexAsync will hit the cache and return immediately.
                    await ServeAutoIndexAsync(session, request, fullPath).ConfigureAwait(false);
                    return;
                }
            }

            // directory ?
            if (Directory.Exists(fullPath)) {
                string defaultFile = Path.Combine(fullPath, DefaultDocument);
                if (File.Exists(defaultFile)) {
                    await ServeFileAsync(session, request, defaultFile).ConfigureAwait(false);
                    return;
                }

                if (AutoIndex) {
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
        }

        #region path resolution and security

        /// <summary>
        /// TryResolvePath
        /// </summary>
        /// <param name="session"></param>
        /// <param name="fullPath"></param>
        /// <param name="isDirectory"></param>
        /// <returns></returns>
        private bool TryResolvePath(HttpSession session, out string fullPath, out bool endsWithSlash) {
            fullPath = string.Empty;
            endsWithSlash = false;

            string pathOnly = session.Request.Path;
            endsWithSlash = pathOnly.EndsWith("/", StringComparison.Ordinal);

            // remove prefix
            string relativePath = pathOnly.Length == _prefix.Length ? "" : pathOnly[_prefix.Length..];

            // URL decode (safe)
            relativePath = Uri.UnescapeDataString(relativePath);

            // normalize slashes
            relativePath = relativePath.Replace('\\', '/');
            if (relativePath.StartsWith('/')) {
                relativePath = relativePath[1..];
            }

            // map to filesystem
            string combined = Path.Combine(_rootPathFull, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string combinedFull = Path.GetFullPath(combined);

            // anti traversal
            if (!combinedFull.StartsWith(_rootPathFull, StringComparison.Ordinal)) {
                return false;
            }

            fullPath = combinedFull;
            return true;
        }

        /// <summary>
        /// normalize prefix to "/xxx" (no trailing slash unless it's "/")
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns></returns>
        private static string NormalizePrefix(string prefix) {
            prefix = prefix.Trim();
            if (!prefix.StartsWith('/')) {
                prefix = "/" + prefix;
            }
            if (prefix.Length > 1 && prefix.EndsWith('/')) {
                prefix = prefix.TrimEnd('/');
            }
            return prefix;
        }

        #endregion path resolution and security

        #region serving

        /// <summary>
        /// ServeFileAsync
        /// </summary>
        /// <param name="session"></param>
        /// <param name="request"></param>
        /// <param name="filePath"></param>
        /// <returns></returns>
        private async ValueTask ServeFileAsync(HttpSession session, HttpRequest request, string filePath) {

            // Cache ON (server from cache first, fallback disk on miss)
            if (_cacheTimeout.HasValue) {

                // 1) cache hit
                if (_cache.TryGetValue(filePath, out var entry) && !entry.IsExpired) {

                    // 304 based on cache metadata
                    if (TryGetIfModifiedSince(request, out DateTimeOffset ims) && ims >= entry.LastModifiedUtc) {
                        await SendNotModifiedAsync(session, entry.LastModifiedUtc).ConfigureAwait(false);
                        return;
                    }

                    await SendBytesAsync(
                        session,
                        contentType: entry.ContentType,
                        body: entry.Data.AsMemory(0, entry.Length),
                        lastModifiedUtc: entry.LastModifiedUtc,
                        cacheSeconds: (int)_cacheTimeout.Value.TotalSeconds
                    ).ConfigureAwait(false);
                    return;
                }

                // 2) cache miss => fallback disk (verify exists + load)
                FileInfo fi;
                try {
                    fi = new FileInfo(filePath);
                }
                catch {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                if (!fi.Exists) {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                DateTimeOffset lastModified = fi.LastWriteTimeUtc;
                string contentType = HttpResponse.DefaultContentType(fi.Extension);

                byte[] data;
                int len;
                try {
                    data = File.ReadAllBytes(fi.FullName);
                    len = data.Length;
                }
                catch (UnauthorizedAccessException) {
                    await SendForbiddenAsync(session).ConfigureAwait(false);
                    return;
                }
                catch {
                    await SendNotFoundAsync(session).ConfigureAwait(false);
                    return;
                }

                // 3) fill cache
                _cache[filePath] = new CacheEntry(
                    data,
                    len,
                    contentType,
                    lastModified,
                    DateTimeOffset.UtcNow + _cacheTimeout.Value
                );

                // 304 after load
                if (TryGetIfModifiedSince(request, out var ims2) && ims2 >= lastModified) {
                    await SendNotModifiedAsync(session, lastModified).ConfigureAwait(false);
                    return;
                }

                // 4) serve
                await SendBytesAsync(
                    session,
                    contentType: contentType,
                    body: data.AsMemory(0, len),
                    lastModifiedUtc: lastModified,
                    cacheSeconds: (int)_cacheTimeout.Value.TotalSeconds
                ).ConfigureAwait(false);

                return;
            }

            // Cache OFF (stream from disk)
            FileInfo fi2;
            try {
                fi2 = new FileInfo(filePath);
            }
            catch {
                await SendNotFoundAsync(session).ConfigureAwait(false);
                return;
            }

            if (!fi2.Exists) {
                await SendNotFoundAsync(session).ConfigureAwait(false);
                return;
            }

            DateTimeOffset lastModified2 = fi2.LastWriteTimeUtc;

            if (TryGetIfModifiedSince(request, out DateTimeOffset ims3) && ims3 >= lastModified2) {
                await SendNotModifiedAsync(session, lastModified2).ConfigureAwait(false);
                return;
            }

            string ct2 = HttpResponse.DefaultContentType(fi2.Extension);
            await SendFileStreamAsync(session, fi2, ct2, lastModified2).ConfigureAwait(false);
        }

        /// <summary>
        /// Send File Stream
        /// </summary>
        /// <param name="session"></param>
        /// <param name="fi"></param>
        /// <param name="contentType"></param>
        /// <param name="lastModifiedUtc"></param>
        /// <returns></returns>
        private async ValueTask SendFileStreamAsync(HttpSession session, FileInfo fi, string contentType, DateTimeOffset lastModifiedUtc) {
            long length = fi.Length;

            session.Response
                   .AddHeader("Content-Type", contentType)
                   .AddHeader("Content-Length", length.ToString())
                   .AddHeader("Last-Modified", lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
                   .AddHeader("Cache-Control", "no-cache");

            // headers only
            if (session.Request.Method == "HEAD") {
                await session.Response
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
            if (_cacheTimeout.HasValue) {
                if (_dirIndexCache.TryGetValue(dirPath, out var cached) && !cached.IsExpired) {
                    await SendBytesAsync(
                        session,
                        contentType: "text/html; charset=utf-8",
                        body: cached.Html.AsMemory(0, cached.Length),
                        lastModifiedUtc: DateTimeOffset.UtcNow,
                        cacheSeconds: null
                    ).ConfigureAwait(false);
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
            if (request.Path != "/" && request.Path.StartsWith(_prefix, StringComparison.Ordinal)) {
                string parent = request.Path.TrimEnd('/');
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
            if (_cacheTimeout.HasValue) {
                _dirIndexCache[dirPath] = new DirIndexCacheEntry(
                    Html: html,
                    Length: html.Length,
                    ExpiresUtc: DateTimeOffset.UtcNow + _cacheTimeout.Value
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

        private static string EnsureTrailingSlash(string p) => p.EndsWith('/') ? p : p + "/";

        private static string JoinUrl(string basePath, string segment) {
            if (string.IsNullOrEmpty(basePath)) {
                return "/" + segment;
            }
            if (!basePath.EndsWith('/')) {
                basePath += "/";
            }
            return basePath + Uri.EscapeDataString(segment);
        }

        #endregion serving

        #region response

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
        /// <returns></returns>
        private async ValueTask SendNotModifiedAsync(HttpSession session, DateTimeOffset lastModifiedUtc) {
            await session.Response
                         .Status(304)
                         .AddHeader("Content-Length", "0")
                         .AddHeader("Last-Modified", lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
                         .AddHeader("Cache-Control", "no-cache")
                         .SendAsync()
                         .ConfigureAwait(false);
        }

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
                   .AddHeader("Content-Length", len.ToString())
                   .AddHeader("Last-Modified", lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
                   .AddHeader("Cache-Control", (cacheSeconds.HasValue && cacheSeconds.Value > 0) ? $"public, max-age={cacheSeconds.Value}" : "no-cache");

            if (contentType != null) {
                session.Response.AddHeader("Content-Type", contentType);
            }

            if (session.Request.Method == "HEAD" || len == 0) {
                await session.Response
                         .SendAsync()
                         .ConfigureAwait(false);
            }
            else {
                await session.Response
                             .Body(body)
                             .SendAsync()
                             .ConfigureAwait(false);
            }
        }

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

        #endregion response

        #region watcher and cache invalidation

        /// <summary>
        /// Start File System Watcher
        /// </summary>
        private void StartWatcher() {
            // best effort
            try {
                if (!Directory.Exists(_rootPathFull)) {
                    return;
                }

                _watcher = new FileSystemWatcher(_rootPathFull, _filter) {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName
                };

                _watcher.Changed += OnFsChanged;
                _watcher.Created += OnFsChanged;
                _watcher.Deleted += OnFsChanged;
                _watcher.Renamed += OnFsRenamed;
                _watcher.Error += (_, __) => { _cache.Clear(); _dirIndexCache.Clear(); };

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
            _cache.TryRemove(e.FullPath, out _);

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
                _cache.TryRemove(e.OldFullPath, out _);

                string? oldParent = Path.GetDirectoryName(e.OldFullPath);
                if (!string.IsNullOrEmpty(oldParent)) {
                    _dirIndexCache.TryRemove(oldParent, out _);
                }
            }
            if (!string.IsNullOrWhiteSpace(e.FullPath)) {
                _cache.TryRemove(e.FullPath, out _);

                string? newParent = Path.GetDirectoryName(e.FullPath);
                if (!string.IsNullOrEmpty(newParent)) {
                    _dirIndexCache.TryRemove(newParent, out _);
                }
            }
        }

        /// <summary>
        /// Cache Entry
        /// </summary>
        /// <param name="Data"></param>
        /// <param name="Length"></param>
        /// <param name="ContentType"></param>
        /// <param name="LastModifiedUtc"></param>
        /// <param name="ExpiresUtc"></param>
        private readonly record struct CacheEntry(
            byte[] Data,
            int Length,
            string ContentType,
            DateTimeOffset LastModifiedUtc,
            DateTimeOffset ExpiresUtc
        ) {
            public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresUtc;
        }

        /// <summary>
        /// DirIndex Cache Entry
        /// </summary>
        /// <param name="Html"></param>
        /// <param name="Length"></param>
        /// <param name="ExpiresUtc"></param>
        private readonly record struct DirIndexCacheEntry(
            byte[] Html,
            int Length,
            DateTimeOffset ExpiresUtc
        ) {
            public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresUtc;
        }

        #endregion watcher and cache invalidation

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

    }

}
