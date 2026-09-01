using System.Threading;

namespace SimpleW.Service.FileBrowser {

    internal sealed class QueuedOperation : IDisposable {

        private readonly CancellationTokenSource _cancellation = new();
        private int _state;

        public Guid Id { get; }
        public string Kind { get; }
        public string Path { get; }
        public Func<CancellationToken, OperationResult> Work { get; }
        public CancellationToken Token => _cancellation.Token;
        public QueuedOperationState State => (QueuedOperationState)Volatile.Read(ref _state);
        public bool IsTerminal => State is QueuedOperationState.Completed or QueuedOperationState.Failed or QueuedOperationState.Cancelled;

        public QueuedOperation(Guid id, string kind, string path, Func<CancellationToken, OperationResult> work) {
            Id = id;
            Kind = kind;
            Path = path;
            Work = work;
            _state = (int)QueuedOperationState.Queued;
        }

        public bool Cancel() {
            if (IsTerminal || _cancellation.IsCancellationRequested) {
                return false;
            }

            try {
                _cancellation.Cancel();
                return true;
            }
            catch (ObjectDisposedException) {
                return false;
            }
        }

        public void MarkRunning() => Volatile.Write(ref _state, (int)QueuedOperationState.Running);
        public void MarkCompleted() => Volatile.Write(ref _state, (int)QueuedOperationState.Completed);
        public void MarkFailed() => Volatile.Write(ref _state, (int)QueuedOperationState.Failed);
        public void MarkCancelled() => Volatile.Write(ref _state, (int)QueuedOperationState.Cancelled);

        public void Dispose() {
            _cancellation.Dispose();
        }

    }

    internal enum QueuedOperationState {
        Queued = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

}
