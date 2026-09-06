namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Options for the file browser module.
    /// </summary>
    public sealed class FileBrowserOptions {

        #region public options

        /// <summary>
        /// Root directory exposed by the browser.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// URL prefix used by the browser.
        /// </summary>
        public string Prefix { get; set; } = "/files";

        /// <summary>
        /// Common authorization callback. Return true to allow the request to proceed to any configured capability check.
        /// </summary>
        public Func<HttpSession, bool>? Authorize { get; set; }

        /// <summary>
        /// Returns the stable owner key used to isolate events, operations and uploads.
        /// Defaults to the authenticated principal identifier, email or name, and to "anonymous" otherwise.
        /// </summary>
        public Func<HttpSession, string>? ScopeKey { get; set; }

        /// <summary>
        /// Allows directory listing and access to the browser UI.
        /// Falls back to <see cref="Authorize"/> or <see cref="AllowAnonymous"/> when not configured.
        /// </summary>
        public Func<HttpSession, bool>? CanList { get; set; }

        /// <summary>
        /// Allows file downloads.
        /// </summary>
        public Func<HttpSession, bool>? CanDownload { get; set; }

        /// <summary>
        /// Allows creation and use of upload sessions.
        /// </summary>
        public Func<HttpSession, bool>? CanUpload { get; set; }

        /// <summary>
        /// Allows folders, files and archives to be created, renamed, moved or extracted.
        /// </summary>
        public Func<HttpSession, bool>? CanModify { get; set; }

        /// <summary>
        /// Allows entries to be moved to the trash.
        /// </summary>
        public Func<HttpSession, bool>? CanDelete { get; set; }

        /// <summary>
        /// Allows trash listing, restoration, permanent deletion and emptying.
        /// </summary>
        public Func<HttpSession, bool>? CanManageTrash { get; set; }

        /// <summary>
        /// Restricts access to a normalized browser-relative path after a capability check succeeds.
        /// </summary>
        public Func<HttpSession, string, bool>? CanAccessPath { get; set; }

        /// <summary>
        /// Allows access without an authorization callback. Explicit capability callbacks can still restrict individual actions.
        /// </summary>
        public bool AllowAnonymous { get; set; }

        /// <summary>
        /// Serves the web UI from the embedded client resources or a development directory.
        /// </summary>
        public bool ServeUi { get; set; } = true;

        /// <summary>
        /// Optional client directory. Overrides embedded resources in every build configuration.
        /// </summary>
        public string? ClientPath { get; set; }

        /// <summary>
        /// Enables the FileBrowser SSE endpoint.
        /// </summary>
        public bool EnableEvents { get; set; } = true;

        /// <summary>
        /// SSE endpoint used by the web UI. Defaults to Prefix + "/api/events".
        /// </summary>
        public string? EventsPrefix { get; set; }

        /// <summary>
        /// Maximum size of a single logical file.
        /// </summary>
        public long MaxFileBytes { get; set; } = 10L * 1024 * 1024 * 1024;

        /// <summary>
        /// Maximum total size for a logical upload session.
        /// </summary>
        public long MaxUploadBytes { get; set; } = 50L * 1024 * 1024 * 1024;

        /// <summary>
        /// Maximum size of one file extracted from an archive.
        /// </summary>
        public long MaxExtractedFileBytes { get; set; } = 10L * 1024 * 1024 * 1024;

        /// <summary>
        /// Maximum combined size of all files extracted from one archive.
        /// </summary>
        public long MaxExtractedBytes { get; set; } = 50L * 1024 * 1024 * 1024;

        /// <summary>
        /// Maximum number of entries allowed in one archive.
        /// </summary>
        public int MaxArchiveEntries { get; set; } = 10000;

        /// <summary>
        /// Files larger than this threshold are uploaded in chunks.
        /// </summary>
        public long UploadChunkThresholdBytes { get; set; } = 100L * 1024 * 1024;

        /// <summary>
        /// Size of one uploaded chunk.
        /// </summary>
        public long UploadChunkBytes { get; set; } = 16L * 1024 * 1024;

        /// <summary>
        /// Maximum inactivity duration before an upload session and its temporary files are removed.
        /// </summary>
        public TimeSpan UploadSessionTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Maximum number of upload sessions tracked at the same time.
        /// </summary>
        public int MaxConcurrentUploadSessions { get; set; } = 100;

        /// <summary>
        /// Duration for which completed, failed or cancelled operations remain queryable.
        /// </summary>
        public TimeSpan OperationHistoryTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Default number of entries returned by one list request.
        /// </summary>
        public int DefaultPageSize { get; set; } = 100;

        /// <summary>
        /// Maximum number of entries returned by one list request.
        /// </summary>
        public int MaxPageSize { get; set; } = 1000;

        /// <summary>
        /// Directory used as trash. Defaults to Path/.trash.
        /// </summary>
        public string? TrashPath { get; set; }

        #endregion public options

        #region normalized options

        internal string NormalizedPath { get; private set; } = string.Empty;
        internal string NormalizedTrashPath { get; private set; } = string.Empty;
        internal string NormalizedPrefix { get; private set; } = string.Empty;
        internal string NormalizedEventsPrefix { get; private set; } = string.Empty;
        internal string? NormalizedClientPath { get; private set; }

        #endregion normalized options

        #region validation

        /// <summary>
        /// Validates option combinations and computes the normalized runtime values.
        /// </summary>
        internal FileBrowserOptions ValidateAndNormalize() {
            if (string.IsNullOrWhiteSpace(Path)) {
                throw new ArgumentException($"{nameof(FileBrowserOptions)}.{nameof(Path)} must not be null or empty.", nameof(Path));
            }
            if (string.IsNullOrWhiteSpace(Prefix)) {
                throw new ArgumentException($"{nameof(FileBrowserOptions)}.{nameof(Prefix)} must not be null or empty.", nameof(Prefix));
            }
            if (MaxFileBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxFileBytes), "Must be > 0.");
            }
            if (MaxUploadBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxUploadBytes), "Must be > 0.");
            }
            if (MaxUploadBytes < MaxFileBytes) {
                throw new ArgumentException($"{nameof(MaxUploadBytes)} must be greater than or equal to {nameof(MaxFileBytes)}.");
            }
            if (MaxExtractedFileBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxExtractedFileBytes), "Must be > 0.");
            }
            if (MaxExtractedBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxExtractedBytes), "Must be > 0.");
            }
            if (MaxExtractedBytes < MaxExtractedFileBytes) {
                throw new ArgumentException($"{nameof(MaxExtractedBytes)} must be greater than or equal to {nameof(MaxExtractedFileBytes)}.");
            }
            if (MaxArchiveEntries <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxArchiveEntries), "Must be > 0.");
            }
            if (UploadChunkThresholdBytes < 0) {
                throw new ArgumentOutOfRangeException(nameof(UploadChunkThresholdBytes), "Must be >= 0.");
            }
            if (UploadChunkBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(UploadChunkBytes), "Must be > 0.");
            }
            if (UploadSessionTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(UploadSessionTimeout), "Must be > 0.");
            }
            if (MaxConcurrentUploadSessions <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxConcurrentUploadSessions), "Must be > 0.");
            }
            if (OperationHistoryTimeout <= TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(OperationHistoryTimeout), "Must be > 0.");
            }
            if (DefaultPageSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(DefaultPageSize), "Must be > 0.");
            }
            if (MaxPageSize <= 0) {
                throw new ArgumentOutOfRangeException(nameof(MaxPageSize), "Must be > 0.");
            }
            if (DefaultPageSize > MaxPageSize) {
                throw new ArgumentException($"{nameof(DefaultPageSize)} must be lower than or equal to {nameof(MaxPageSize)}.");
            }
            if (!AllowAnonymous && Authorize == null && !HasCapabilityCallback()) {
                throw new ArgumentException($"{nameof(Authorize)} or at least one capability callback must be configured unless {nameof(AllowAnonymous)} is true.");
            }

            NormalizedPath = NormalizeDirectory(Path);
            NormalizedTrashPath = NormalizeDirectory(string.IsNullOrWhiteSpace(TrashPath)
                ? System.IO.Path.Combine(NormalizedPath, ".trash")
                : TrashPath!);
            NormalizedClientPath = string.IsNullOrWhiteSpace(ClientPath) ? null : NormalizeDirectory(ClientPath!);
            NormalizedPrefix = SimpleWExtension.NormalizePrefix(Prefix);
            NormalizedEventsPrefix = SimpleWExtension.NormalizePrefix(
                string.IsNullOrWhiteSpace(EventsPrefix)
                    ? (NormalizedPrefix == "/" ? "/api/events" : NormalizedPrefix + "/api/events")
                    : EventsPrefix!
            );
            if (EnableEvents && NormalizedEventsPrefix == "/") {
                throw new ArgumentException($"{nameof(EventsPrefix)} must not be '/'.", nameof(EventsPrefix));
            }

            return this;
        }

        /// <summary>
        /// Reports whether granular authorization has been configured.
        /// </summary>
        private bool HasCapabilityCallback() {
            return CanList != null
                || CanDownload != null
                || CanUpload != null
                || CanModify != null
                || CanDelete != null
                || CanManageTrash != null;
        }

        /// <summary>
        /// Returns an absolute directory path ending with a platform separator.
        /// </summary>
        private static string NormalizeDirectory(string path) {
            string full = System.IO.Path.GetFullPath(path);
            if (!full.EndsWith(System.IO.Path.DirectorySeparatorChar) && !full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)) {
                full += System.IO.Path.DirectorySeparatorChar;
            }
            return full;
        }

        #endregion validation

    }

}
