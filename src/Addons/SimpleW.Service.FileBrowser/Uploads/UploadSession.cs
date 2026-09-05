using System.Threading;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Holds the shared state and cancellation token of a logical upload session.
    /// </summary>
    internal sealed class UploadSession {

        #region fields and properties

        private readonly CancellationTokenSource _cancellation = new();

        public Guid Id { get; }
        public Dictionary<string, UploadFileState> Files { get; }
        public long TotalBytes { get; }
        public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;
        public CancellationToken Token => _cancellation.Token;
        public bool IsCancellationRequested => _cancellation.IsCancellationRequested;
        public bool IsComplete => Files.Values.All(static f => f.Completed);

        #endregion fields and properties

        /// <summary>
        /// Creates an upload session for the declared files.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="files"></param>
        /// <param name="totalBytes"></param>
        public UploadSession(Guid id, Dictionary<string, UploadFileState> files, long totalBytes) {
            Id = id;
            Files = files;
            TotalBytes = totalBytes;
        }

        /// <summary>
        /// Requests cancellation and reports whether this call changed the session state.
        /// </summary>
        public bool Cancel() {
            if (_cancellation.IsCancellationRequested) {
                return false;
            }

            _cancellation.Cancel();
            return true;
        }

    }

}
