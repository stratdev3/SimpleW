using System.Threading;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Represents a cancellable file-system operation waiting for background execution.
    /// </summary>
    internal sealed class QueuedOperation : IDisposable {

        #region fields and properties

        private readonly CancellationTokenSource _cancellation = new();
        private int _cancellationRequested;
        private int _state;

        public Guid Id { get; }
        public string OwnerKey { get; }
        public string Kind { get; }
        public string Path { get; }
        public Func<CancellationToken, OperationResult> Work { get; }
        public CancellationToken Token => _cancellation.Token;
        public bool IsCancellationRequested => Volatile.Read(ref _cancellationRequested) != 0;
        public QueuedOperationState State => (QueuedOperationState)Volatile.Read(ref _state);
        public bool IsTerminal => State is QueuedOperationState.Completed or QueuedOperationState.Failed or QueuedOperationState.Cancelled;
        public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? StartedAtUtc { get; private set; }
        public DateTimeOffset? CompletedAtUtc { get; private set; }
        public object? Payload { get; private set; }
        public string? Error { get; private set; }

        #endregion fields and properties

        /// <summary>
        /// Creates an operation in the queued state.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="ownerKey"></param>
        /// <param name="kind"></param>
        /// <param name="path"></param>
        /// <param name="work"></param>
        public QueuedOperation(Guid id, string ownerKey, string kind, string path, Func<CancellationToken, OperationResult> work) {
            Id = id;
            OwnerKey = ownerKey;
            Kind = kind;
            Path = path;
            Work = work;
            _state = (int)QueuedOperationState.Queued;
        }

        #region lifecycle

        /// <summary>
        /// Requests cancellation unless the operation has already reached a terminal state.
        /// </summary>
        public bool Cancel() {
            if (IsTerminal || _cancellation.IsCancellationRequested) {
                return false;
            }

            try {
                _cancellation.Cancel();
                Volatile.Write(ref _cancellationRequested, 1);
                return true;
            }
            catch (ObjectDisposedException) {
                return false;
            }
        }

        /// <summary>
        /// Marks the operation as running.
        /// </summary>
        public void MarkRunning() {
            StartedAtUtc = DateTimeOffset.UtcNow;
            Volatile.Write(ref _state, (int)QueuedOperationState.Running);
        }

        /// <summary>
        /// Marks the operation as completed.
        /// </summary>
        public void MarkCompleted(object? payload) {
            Payload = payload;
            CompletedAtUtc = DateTimeOffset.UtcNow;
            Volatile.Write(ref _state, (int)QueuedOperationState.Completed);
        }

        /// <summary>
        /// Marks the operation as failed.
        /// </summary>
        public void MarkFailed(string error) {
            Error = error;
            CompletedAtUtc = DateTimeOffset.UtcNow;
            Volatile.Write(ref _state, (int)QueuedOperationState.Failed);
        }

        /// <summary>
        /// Marks the operation as cancelled.
        /// </summary>
        public void MarkCancelled() {
            Volatile.Write(ref _cancellationRequested, 1);
            CompletedAtUtc = DateTimeOffset.UtcNow;
            Volatile.Write(ref _state, (int)QueuedOperationState.Cancelled);
        }

        /// <summary>
        /// Releases the cancellation source owned by the operation.
        /// </summary>
        public void Dispose() {
            _cancellation.Dispose();
        }

        #endregion lifecycle

    }

    /// <summary>
    /// Identifies the current lifecycle state of a queued operation.
    /// </summary>
    internal enum QueuedOperationState {
        Queued = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

}
