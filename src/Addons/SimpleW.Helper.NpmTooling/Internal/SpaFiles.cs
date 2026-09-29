namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Serves production frontend assets and provides an HTML navigation fallback outside API routes.
    /// </summary>
    internal sealed class SpaFiles {
        private readonly string _root;
        private readonly string _apiPrefix;
        private readonly StringComparison _comparison = OperatingSystem.IsWindows()
                                                            ? StringComparison.OrdinalIgnoreCase
                                                            : StringComparison.Ordinal;

        /// <summary>
        /// Resolves the production root and requires a safe index.html before installing the fallback.
        /// </summary>
        /// <param name="options">Production asset directory and API prefix.</param>
        public SpaFiles(NpmSpaOptions options) {
            _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.ProductionPath, AppContext.BaseDirectory));
            _apiPrefix = options.ApiPrefix.TrimEnd('/');

            if (!_apiPrefix.StartsWith('/') || _apiPrefix.Length < 2) {
                throw new ArgumentException("Spa.ApiPrefix must be an absolute URL path such as /api.");
            }
            if (!TryResolve("index.html", out string index) || !File.Exists(index)) {
                throw new DirectoryNotFoundException($"SimpleW.Helper.NpmTooling: compiled client missing or unsafe at '{_root}'. Publish the client first.");
            }
        }

        /// <summary>
        /// Registers the frontend handler as the router's root fallback.
        /// </summary>
        /// <param name="server">Server that will serve the production frontend.</param>
        public void Install(SimpleWServer server) {
            server.Router.MapFallback((HttpSession session) => HandleAsync(session));
        }

        /// <summary>
        /// Serves a safe static file or the SPA shell for eligible navigation requests, otherwise returning 404.
        /// </summary>
        /// <param name="session">HTTP session whose unmatched request is being handled.</param>
        /// <returns>A task that completes when the response is sent.</returns>
        private async ValueTask HandleAsync(HttpSession session) {
            string path = session.Request.Path;
            bool canRead = session.Request.Method is "GET" or "HEAD";
            if (!canRead || IsApi(path)) {
                await NotFoundAsync(session).ConfigureAwait(false);
                return;
            }
            string relative;
            try { relative = Uri.UnescapeDataString(path).TrimStart('/'); }
            catch (UriFormatException) {
                await NotFoundAsync(session).ConfigureAwait(false);
                return;
            }
            string decodedPath = '/' + relative;
            if (IsApi('/' + relative) || !TryResolve(relative, out string file)) {
                await NotFoundAsync(session).ConfigureAwait(false);
                return;
            }
            if (Directory.Exists(file)) {
                relative = relative.TrimEnd('/') + "/index.html";
                if (!TryResolve(relative.TrimStart('/'), out file)) {
                    await NotFoundAsync(session).ConfigureAwait(false);
                    return;
                }
            }
            if (File.Exists(file)) {
                await session.Response.File(file).SendAsync().ConfigureAwait(false);
                return;
            }
            // A missing asset must remain a 404 even if the browser sends Accept: text/html.
            bool navigation = !Path.HasExtension(decodedPath)
                              && !decodedPath.Equals("/assets", StringComparison.OrdinalIgnoreCase)
                              && !decodedPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                              && AcceptsHtml(session.Request.Headers.Accept);
            if (navigation && TryResolve("index.html", out string index) && File.Exists(index)) {
                await session.Response.File(index).SendAsync().ConfigureAwait(false);
                return;
            }
            await NotFoundAsync(session).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks the API prefix boundary without treating similarly named routes as API paths.
        /// </summary>
        /// <param name="path">Request path to compare with the configured API prefix.</param>
        /// <returns>True for the API prefix itself or a path below it, ignoring case.</returns>
        private bool IsApi(string path) => path.Equals(_apiPrefix, StringComparison.OrdinalIgnoreCase)
                                           || path.StartsWith(_apiPrefix + '/', StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Checks for an explicit text/html media range that is not disabled by a nonpositive quality value.
        /// </summary>
        /// <param name="accept">Accept header value, or null when the header is absent.</param>
        /// <returns>True when the header allows the HTML navigation fallback.</returns>
        private static bool AcceptsHtml(string? accept) => accept?.Split(',').Any(part => {
            string[] values = part.Split(';', StringSplitOptions.TrimEntries);
            return values[0].Equals("text/html", StringComparison.OrdinalIgnoreCase)
                   && !values.Skip(1).Any(value => value.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                      && double.TryParse(value[2..], System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out double quality) && quality <= 0);
        }) == true;

        /// <summary>
        /// Resolves a path inside the production root, rejecting hidden segments, traversal and links in its ancestry.
        /// </summary>
        /// <param name="relative">Decoded path relative to the production directory.</param>
        /// <param name="result">Resolved path; use this value only when the method returns true.</param>
        /// <returns>True when the path passes validation, whether or not the target file exists.</returns>
        private bool TryResolve(string relative, out string result) {
            result = string.Empty;
            // Reject ambiguous paths and Windows alternate data streams on every platform.
            if (relative.Contains('\\') || relative.Contains(':') || relative.Contains('\0')
                || relative.Split('/').Any(part => part is "." or ".." || part.StartsWith('.'))) {
                return false;
            }
            try {
                result = Path.GetFullPath(Path.Combine(_root, relative));
                if (!result.Equals(_root, _comparison) && !result.StartsWith(_root + Path.DirectorySeparatorChar, _comparison)) {
                    return false;
                }
                // Reject symlinks/junctions in the full path, including ancestors of the public root.
                string? current = result;
                while (current != null) {
                    if ((File.Exists(current) || Directory.Exists(current))
                        && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
                        return false;
                    }
                    current = Path.GetDirectoryName(current);
                }
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) {
                return false;
            }
        }

        /// <summary>
        /// Sends a plain-text 404 response for a request the frontend fallback cannot serve.
        /// </summary>
        /// <param name="session">HTTP session receiving the response.</param>
        /// <returns>A task that completes when the response is sent.</returns>
        private static ValueTask NotFoundAsync(HttpSession session) => session.Response.Status(404).Text("Not Found").SendAsync();

    }

}
