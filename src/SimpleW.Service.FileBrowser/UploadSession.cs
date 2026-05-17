using System.Threading;

namespace SimpleW.Service.FileBrowser {

    internal sealed class UploadSession {

        private readonly CancellationTokenSource _cancellation = new();

        public Guid Id { get; }
        public Dictionary<string, UploadFileState> Files { get; }
        public long TotalBytes { get; }
        public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;
        public CancellationToken Token => _cancellation.Token;
        public bool IsCancellationRequested => _cancellation.IsCancellationRequested;
        public bool IsComplete => Files.Values.All(static f => f.Completed);

        public UploadSession(Guid id, Dictionary<string, UploadFileState> files, long totalBytes) {
            Id = id;
            Files = files;
            TotalBytes = totalBytes;
        }

        public bool Cancel() {
            if (_cancellation.IsCancellationRequested) {
                return false;
            }

            _cancellation.Cancel();
            return true;
        }

    }

}
