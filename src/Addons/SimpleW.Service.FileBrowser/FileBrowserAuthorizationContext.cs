namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Identifies the business permission being authorized, independently of its HTTP route or technical stage.
    /// </summary>
    public enum FileBrowserAction {

        #region general access

        /// <summary>
        /// Common gate, evaluated first on every request with no resources. Covers UI, configuration and SSE access.
        /// </summary>
        AccessModule = 0,

        #endregion general access

        #region listing

        /// <summary>
        /// Authorize listing the requested directory, including all of its visible entries.
        /// </summary>
        List = 4,

        /// <summary>
        /// Authorize listing the trash, without per-entry authorization checks.
        /// </summary>
        ListTrash = 12,

        #endregion listing

        #region actions

        /// <summary>
        /// Download a file, including HEAD requests, or calculate its checksum.
        /// </summary>
        Download = 6,

        /// <summary>
        /// Create, inspect, receive, complete or cancel an upload session and its files.
        /// </summary>
        Upload = 19,

        /// <summary>
        /// Create a directory and any missing parents.
        /// </summary>
        CreateFolder = 8,

        /// <summary>
        /// Rename an entry and its descendants.
        /// </summary>
        Rename = 9,

        /// <summary>
        /// Move an entry and its descendants.
        /// </summary>
        Move = 10,

        /// <summary>
        /// Move entries and their descendants to the trash.
        /// </summary>
        Delete = 11,

        /// <summary>
        /// Restore trash entries and their descendants.
        /// </summary>
        RestoreTrash = 14,

        /// <summary>
        /// Permanently delete selected trash entries and their descendants.
        /// </summary>
        PurgeTrash = 15,

        /// <summary>
        /// Permanently delete every trash entry and its descendants.
        /// </summary>
        EmptyTrash = 16,

        /// <summary>
        /// Create an archive from the specified sources.
        /// </summary>
        Archive = 17,

        /// <summary>
        /// Extract an archive into the specified destinations, including all source volumes.
        /// </summary>
        Extract = 18,

        #endregion actions

    }

    /// <summary>
    /// One logical resource. Paths use '/' relative to the browser root; an empty path is the root.
    /// Physical storage paths are never exposed. A null path denotes a legacy trash entry without an original location.
    /// </summary>
    /// <param name="Path">Source or target path; for creation, the path to create.</param>
    /// <param name="DestinationPath">Destination paired with a source for move, rename, restore, archive or extract; otherwise null.</param>
    /// <param name="IsDirectory">Whether the resource is a directory, or null when its type is unknown.</param>
    /// <param name="TrashId">Trash entry identifier, or null for a live resource.</param>
    /// <param name="TrashRelativePath">Path inside the trash payload; empty for its root, null for a live resource.</param>
    public sealed record FileBrowserAuthorizationResource(
        string? Path,
        string? DestinationPath = null,
        bool? IsDirectory = null,
        string? TrashId = null,
        string? TrashRelativePath = null
    );

    /// <summary>
    /// Immutable authorization input. Each valid request checks AccessModule, then its business action once.
    /// Recursive modifications include the complete resource set. Listing includes only the requested directory.
    /// Return Forbidden to reject the entire action, or Challenge to request authentication.
    /// </summary>
    public sealed class FileBrowserAuthorizationContext {

        /// <summary>
        /// The business permission being authorized. Operation status and cancellation reuse the original operation permission.
        /// </summary>
        public FileBrowserAction Action { get; }

        /// <summary>An immutable snapshot of the resources involved in this authorization check.
        /// </summary>
        public IReadOnlyList<FileBrowserAuthorizationResource> Resources { get; }

        /// <summary>
        /// The related upload identifier, when available.
        /// </summary>
        public Guid? UploadId { get; }

        /// <summary>
        /// The related operation identifier, when available.
        /// </summary>
        public Guid? OperationId { get; }

        /// <summary>
        /// FileBrowserAuthorizationContext
        /// </summary>
        /// <param name="action"></param>
        /// <param name="resources"></param>
        /// <param name="uploadId"></param>
        /// <param name="operationId"></param>
        internal FileBrowserAuthorizationContext(
            FileBrowserAction action,
            IEnumerable<FileBrowserAuthorizationResource>? resources = null,
            Guid? uploadId = null,
            Guid? operationId = null) {
            Action = action;
            Resources = Array.AsReadOnly(resources?.ToArray() ?? Array.Empty<FileBrowserAuthorizationResource>());
            UploadId = uploadId;
            OperationId = operationId;
        }

    }

}
