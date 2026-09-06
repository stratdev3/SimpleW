using System.Threading;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Holds the shared state and cancellation token of a logical upload session.
    /// </summary>
    internal sealed class UploadSession : IDisposable {

        #region fields and properties

        private readonly CancellationTokenSource _cancellation = new();
        private readonly CancellationToken _token;
        private readonly object _sync = new();
        private DateTimeOffset _lastActivityAtUtc;
        private bool _disposed;

        public Guid Id { get; }
        public string OwnerKey { get; }
        public Dictionary<string, UploadFileState> Files { get; }
        public long TotalBytes { get; }
        public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastActivityAtUtc {
            get {
                lock (_sync) {
                    return _lastActivityAtUtc;
                }
            }
        }
        public CancellationToken Token => _token;
        public bool IsCancellationRequested => _token.IsCancellationRequested;
        public bool IsComplete => Files.Values.All(static f => f.Completed);

        #endregion fields and properties

        /// <summary>
        /// Creates an upload session for the declared files.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ownerKey"></param>
        /// <param name="files"></param>
        /// <param name="totalBytes"></param>
        public UploadSession(Guid id, string ownerKey, Dictionary<string, UploadFileState> files, long totalBytes) {
            Id = id;
            OwnerKey = ownerKey;
            Files = files;
            TotalBytes = totalBytes;
            _lastActivityAtUtc = CreatedAtUtc;
            _token = _cancellation.Token;
        }

        /// <summary>
        /// Refreshes the session activity deadline when it has not expired or been cancelled.
        /// </summary>
        /// <param name="timeout"></param>
        public bool TryTouch(TimeSpan timeout) {
            lock (_sync) {
                if (_disposed || _token.IsCancellationRequested) {
                    return false;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                if (now - _lastActivityAtUtc >= timeout) {
                    _cancellation.Cancel();
                    return false;
                }

                _lastActivityAtUtc = now;
                return true;
            }
        }

        /// <summary>
        /// Cancels the session when its inactivity timeout has elapsed.
        /// </summary>
        /// <param name="now"></param>
        /// <param name="timeout"></param>
        public bool TryExpire(DateTimeOffset now, TimeSpan timeout) {
            lock (_sync) {
                if (_disposed || _token.IsCancellationRequested) {
                    return true;
                }
                if (now - _lastActivityAtUtc < timeout) {
                    return false;
                }

                _cancellation.Cancel();
                return true;
            }
        }

        /// <summary>
        /// Requests cancellation and reports whether this call changed the session state.
        /// </summary>
        public bool Cancel() {
            lock (_sync) {
                if (_disposed || _token.IsCancellationRequested) {
                    return false;
                }

                _cancellation.Cancel();
                return true;
            }
        }

        /// <summary>
        /// Releases the session cancellation source.
        /// </summary>
        public void Dispose() {
            lock (_sync) {
                if (_disposed) {
                    return;
                }

                _disposed = true;
                _cancellation.Dispose();
            }
        }

    }

}
