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

            // resolve FS target
            if (!TryResolvePath(session, out string fullPath, out bool isDirectory)) {
                await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                return;
            }

            if (isDirectory) {
                // directory => default document or auto-index
                string defaultFile = Path.Combine(fullPath, DefaultDocument);
                if (File.Exists(defaultFile)) {
                    await ServeFileAsync(session, request, defaultFile).ConfigureAwait(false);
                    return;
                }

                if (AutoIndex) {
                    await ServeAutoIndexAsync(session, request, fullPath).ConfigureAwait(false);
                    return;
                }

                await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                return;
            }

            // file
            if (!File.Exists(fullPath)) {
                await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                return;
            }

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
        private bool TryResolvePath(HttpSession session, out string fullPath, out bool isDirectory) {
            fullPath = string.Empty;
            isDirectory = false;

            // strip query/fragment (par sécurité si ton parser laisse passer)
            string pathOnly = session.Request.Path;

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

            // anti traversal: must stay under root
            if (!combinedFull.StartsWith(_rootPathFull, StringComparison.Ordinal)) {
                return false;
            }

            // directory request?
            if (Directory.Exists(combinedFull)) {
                isDirectory = true;
                fullPath = combinedFull;
                return true;
            }

            // file request
            if (File.Exists(combinedFull)) {
                isDirectory = false;
                fullPath = combinedFull;
                return true;
            }

            // special case: if request ends with "/" consider directory even if missing
            if (pathOnly.EndsWith("/", StringComparison.Ordinal) && Directory.Exists(combinedFull)) {
                isDirectory = true;
                fullPath = combinedFull;
                return true;
            }

            return false;
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
        private async Task ServeFileAsync(HttpSession session, HttpRequest request, string filePath) {
            FileInfo fi;
            try {
                fi = new FileInfo(filePath);
            }
            catch {
                await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                return;
            }

            if (!fi.Exists) {
                await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                return;
            }

            DateTimeOffset lastModified = fi.LastWriteTimeUtc;

            // 304 If-Modified-Since (simple)
            if (TryGetIfModifiedSince(request, out var ims) && ims >= lastModified) {
                await SendNotModifiedAsync(session, lastModified).ConfigureAwait(false);
                return;
            }

            string contentType = HttpResponse.DefaultContentType(fi.Extension);

            // Cached path?
            if (_cacheTimeout.HasValue) {
                string key = fi.FullName;

                if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired && entry.LastModifiedUtc == lastModified) {
                    // serve from cache
                    await SendBytesAsync(
                        session,
                        statusCode: 200,
                        statusText: "OK",
                        contentType: entry.ContentType,
                        body: entry.Data.AsMemory(0, entry.Length),
                        method: request.Method,
                        lastModifiedUtc: entry.LastModifiedUtc,
                        cacheSeconds: (int)_cacheTimeout.Value.TotalSeconds
                    ).ConfigureAwait(false);
                    return;
                }

                // refresh cache
                byte[] data;
                int len;
                try {
                    // alpha choice: read all. (si tu veux du giga-file, on passera en stream + cache disabled)
                    data = File.ReadAllBytes(fi.FullName);
                    len = data.Length;
                }
                catch (IOException) {
                    await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                    return;
                }
                catch (UnauthorizedAccessException) {
                    await SendForbiddenAsync(session).ConfigureAwait(false);
                    return;
                }

                var newEntry = new CacheEntry(
                    data,
                    len,
                    contentType,
                    lastModified,
                    DateTimeOffset.UtcNow + _cacheTimeout.Value
                );

                _cache[key] = newEntry;

                await SendBytesAsync(
                    session,
                    statusCode: 200,
                    statusText: "OK",
                    contentType: contentType,
                    body: data.AsMemory(0, len),
                    method: request.Method,
                    lastModifiedUtc: lastModified,
                    cacheSeconds: (int)_cacheTimeout.Value.TotalSeconds
                ).ConfigureAwait(false);

                return;
            }

            // No cache: stream
            await SendFileStreamAsync(session, fi, contentType, request.Method, lastModified).ConfigureAwait(false);
        }

        private async Task SendFileStreamAsync(HttpSession session, FileInfo fi, string contentType, string method, DateTimeOffset lastModifiedUtc) {
            long length = fi.Length;

            // headers first
            await SendHeadersAsync(
                session,
                statusCode: 200,
                statusText: "OK",
                contentType: contentType,
                contentLength: length,
                lastModifiedUtc: lastModifiedUtc,
                cacheSeconds: null
            ).ConfigureAwait(false);

            if (method.Equals("HEAD", StringComparison.Ordinal)) {
                return;
            }

            // stream body
            const int chunkSize = 64 * 1024;
            byte[] buffer = _pool.Rent(chunkSize);
            try {
                using FileStream fs = new(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: chunkSize, useAsync: true);

                while (true) {
                    int read = await fs.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read <= 0)
                        break;
                    await session.SendAsync(new ArraySegment<byte>(buffer, 0, read)).ConfigureAwait(false);
                }
            }
            catch {
                // si le client s'est barré, on s'en fout, alpha rules.
            }
            finally {
                _pool.Return(buffer);
            }
        }

        private async Task ServeAutoIndexAsync(HttpSession session, HttpRequest request, string dirPath) {

            // Cache ON ? (on réutilise _cacheTimeout comme TTL d'index, simple)
            if (_cacheTimeout.HasValue) {
                if (_dirIndexCache.TryGetValue(dirPath, out var cached) && !cached.IsExpired) {
                    await SendBytesAsync(
                        session,
                        statusCode: 200,
                        statusText: "OK",
                        contentType: "text/html; charset=utf-8",
                        body: cached.Html.AsMemory(0, cached.Length),
                        method: request.Method,
                        lastModifiedUtc: DateTimeOffset.UtcNow, // ou garde une date, mais c'est de l'index dynamique
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
            sb.Append("</h1><ul>");

            // parent link
            if (request.Path != "/" && request.Path.StartsWith(_prefix, StringComparison.Ordinal)) {
                string parent = request.Path.TrimEnd('/');
                int lastSlash = parent.LastIndexOf('/');
                if (lastSlash >= 0) {
                    parent = parent[..(lastSlash + 1)];
                    sb.Append("<li><a href=\"");
                    sb.Append(HtmlEncode(parent));
                    sb.Append("\">../</a></li>");
                }
            }

            try {
                foreach (var d in Directory.EnumerateDirectories(dirPath)) {
                    string name = Path.GetFileName(d);
                    sb.Append("<li><a href=\"");
                    sb.Append(HtmlEncode(EnsureTrailingSlash(JoinUrl(request.Path, name))));
                    sb.Append("\">");
                    sb.Append(HtmlEncode(name));
                    sb.Append("/</a></li>");
                }

                foreach (var f in Directory.EnumerateFiles(dirPath)) {
                    string name = Path.GetFileName(f);
                    sb.Append("<li><a href=\"");
                    sb.Append(HtmlEncode(JoinUrl(request.Path, name)));
                    sb.Append("\">");
                    sb.Append(HtmlEncode(name));
                    sb.Append("</a></li>");
                }
            }
            catch (UnauthorizedAccessException) {
                await SendForbiddenAsync(session).ConfigureAwait(false);
                return;
            }
            catch {
                await SendNotFoundAsync(session, request.Path).ConfigureAwait(false);
                return;
            }

            sb.Append("</ul><hr><small>SimpleW static index</small></body></html>");

            byte[] html = Encoding.UTF8.GetBytes(sb.ToString());

            await SendBytesAsync(
                session,
                statusCode: 200,
                statusText: "OK",
                contentType: "text/html; charset=utf-8",
                body: html,
                method: request.Method,
                lastModifiedUtc: DateTimeOffset.UtcNow,
                cacheSeconds: null
            ).ConfigureAwait(false);
        }

        private static string EnsureTrailingSlash(string p) => p.EndsWith('/') ? p : p + "/";

        private static string JoinUrl(string basePath, string segment) {
            if (string.IsNullOrEmpty(basePath))
                return "/" + segment;
            if (!basePath.EndsWith('/'))
                basePath += "/";
            return basePath + Uri.EscapeDataString(segment);
        }

        #endregion serving

        #region response


        private async Task SendNotFoundAsync(HttpSession session, string path) {
            await session.Response
                .Status(404, "Not Found")
                .Text($"Not Found: {path}")
                .SendAsync()
                .ConfigureAwait(false);
        }

        private async Task SendForbiddenAsync(HttpSession session) {
            await session.Response
                .Status(403, "Forbidden")
                .Text("Forbidden")
                .SendAsync()
                .ConfigureAwait(false);
        }

        private async Task SendNotModifiedAsync(HttpSession session, DateTimeOffset lastModifiedUtc) {
            // 304 must not include body
            await SendHeadersAsync(
                session,
                statusCode: 304,
                statusText: "Not Modified",
                contentType: null,
                contentLength: 0,
                lastModifiedUtc: lastModifiedUtc,
                cacheSeconds: null
            ).ConfigureAwait(false);
        }

        private async Task SendBytesAsync(
            HttpSession session,
            int statusCode,
            string statusText,
            string? contentType,
            ReadOnlyMemory<byte> body,
            string method,
            DateTimeOffset lastModifiedUtc,
            int? cacheSeconds
        ) {
            long len = body.Length;

            await SendHeadersAsync(
                session,
                statusCode: statusCode,
                statusText: statusText,
                contentType: contentType,
                contentLength: len,
                lastModifiedUtc: lastModifiedUtc,
                cacheSeconds: cacheSeconds
            ).ConfigureAwait(false);

            if (method.Equals("HEAD", StringComparison.Ordinal) || len == 0) {
                return;
            }

            await session.SendAsync(body).ConfigureAwait(false);
        }

        private async Task SendHeadersAsync(
            HttpSession session,
            int statusCode,
            string statusText,
            string? contentType,
            long contentLength,
            DateTimeOffset lastModifiedUtc,
            int? cacheSeconds
        ) {
            // Build headers using your PooledBufferWriter to avoid allocations
            PooledBufferWriter w = new(ArrayPool<byte>.Shared, initialSize: 512);
            try {
                WriteAscii(w, "HTTP/1.1 ");
                WriteIntAscii(w, statusCode);
                WriteAscii(w, " ");
                WriteAscii(w, statusText);
                WriteCRLF(w);

                WriteAscii(w, "Content-Length: ");
                WriteLongAscii(w, contentLength);
                WriteCRLF(w);

                if (!string.IsNullOrEmpty(contentType)) {
                    WriteAscii(w, "Content-Type: ");
                    WriteAscii(w, contentType!);
                    WriteCRLF(w);
                }

                // Last-Modified
                WriteAscii(w, "Last-Modified: ");
                WriteAscii(w, lastModifiedUtc.UtcDateTime.ToString("r", CultureInfo.InvariantCulture));
                WriteCRLF(w);

                // Cache-Control (simple)
                if (cacheSeconds.HasValue && cacheSeconds.Value > 0) {
                    WriteAscii(w, "Cache-Control: public, max-age=");
                    WriteIntAscii(w, cacheSeconds.Value);
                    WriteCRLF(w);
                }
                else {
                    WriteAscii(w, "Cache-Control: no-cache");
                    WriteCRLF(w);
                }

                // Connection: keep-alive/close according to session
                WriteAscii(w, "Connection: ");
                WriteAscii(w, session.CloseAfterResponse ? "close" : "keep-alive");
                WriteCRLF(w);

                // end
                WriteCRLF(w);

                await session.SendAsync(new ArraySegment<byte>(w.Buffer, 0, w.Length)).ConfigureAwait(false);
            }
            finally {
                w.Dispose();
            }
        }

        #region writers

        private static void WriteCRLF(PooledBufferWriter w) {
            Span<byte> span = w.GetSpan(2);
            span[0] = (byte)'\r';
            span[1] = (byte)'\n';
            w.Advance(2);
        }

        private static void WriteAscii(PooledBufferWriter w, string s) {
            if (string.IsNullOrEmpty(s))
                return;
            int max = Encoding.ASCII.GetMaxByteCount(s.Length);
            Span<byte> span = w.GetSpan(max);
            int len = Encoding.ASCII.GetBytes(s.AsSpan(), span);
            w.Advance(len);
        }

        private static void WriteIntAscii(PooledBufferWriter w, int value) {
            Span<char> tmp = stackalloc char[16];
            if (!value.TryFormat(tmp, out int chars)) {
                WriteAscii(w, value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            Span<byte> dst = w.GetSpan(chars);
            for (int i = 0; i < chars; i++)
                dst[i] = (byte)tmp[i];
            w.Advance(chars);
        }

        private static void WriteLongAscii(PooledBufferWriter w, long value) {
            Span<char> tmp = stackalloc char[32];
            if (!value.TryFormat(tmp, out int chars)) {
                WriteAscii(w, value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            Span<byte> dst = w.GetSpan(chars);
            for (int i = 0; i < chars; i++)
                dst[i] = (byte)tmp[i];
            w.Advance(chars);
        }

        #endregion writers

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
                _watcher.Error += (_, __) => _cache.Clear();

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
