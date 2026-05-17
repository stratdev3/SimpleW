namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Options for the file browser module.
    /// </summary>
    public sealed class FileBrowserOptions {

        /// <summary>
        /// Root directory exposed by the browser.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// URL prefix used by the browser.
        /// </summary>
        public string Prefix { get; set; } = "/files";

        /// <summary>
        /// Authorization callback. Return true to allow the request.
        /// </summary>
        public Func<HttpSession, bool>? Authorize { get; set; }

        /// <summary>
        /// Allows access without an authorization callback.
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
        /// Files larger than this threshold are uploaded in chunks.
        /// </summary>
        public long UploadChunkThresholdBytes { get; set; } = 100L * 1024 * 1024;

        /// <summary>
        /// Size of one uploaded chunk.
        /// </summary>
        public long UploadChunkBytes { get; set; } = 16L * 1024 * 1024;

        /// <summary>
        /// Directory used as trash. Defaults to Path/.trash.
        /// </summary>
        public string? TrashPath { get; set; }

        internal string NormalizedPath { get; private set; } = string.Empty;
        internal string NormalizedTrashPath { get; private set; } = string.Empty;
        internal string NormalizedPrefix { get; private set; } = string.Empty;
        internal string NormalizedEventsPrefix { get; private set; } = string.Empty;
        internal string? NormalizedClientPath { get; private set; }

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
            if (UploadChunkThresholdBytes < 0) {
                throw new ArgumentOutOfRangeException(nameof(UploadChunkThresholdBytes), "Must be >= 0.");
            }
            if (UploadChunkBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(UploadChunkBytes), "Must be > 0.");
            }
            if (!AllowAnonymous && Authorize == null) {
                throw new ArgumentException($"{nameof(Authorize)} must be configured unless {nameof(AllowAnonymous)} is true.");
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

        private static string NormalizeDirectory(string path) {
            string full = System.IO.Path.GetFullPath(path);
            if (!full.EndsWith(System.IO.Path.DirectorySeparatorChar) && !full.EndsWith(System.IO.Path.AltDirectorySeparatorChar)) {
                full += System.IO.Path.DirectorySeparatorChar;
            }
            return full;
        }

    }

}
