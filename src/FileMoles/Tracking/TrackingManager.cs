using FileMoles.Events;

namespace FileMoles.Tracking
{
    public class TrackingManager : IDisposable, IAsyncDisposable
    {
        private InternalTrackingManager manager = null!;
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<bool> _initialScanCompletionSource = new();

        internal void Init(InternalTrackingManager manager)
        {
            this.manager = manager;
        }

        internal async Task HandleFileEventAsync(FileSystemEvent e, CancellationToken token)
        {
            await manager.HandleFileEventAsync(e, token);
        }

        internal async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await manager.InitializeAsync(cancellationToken);
            _initialScanCompletionSource.SetResult(true);
        }

        internal async Task SyncTrackingFilesAsync(CancellationToken cancellationToken)
        {
            await manager.SyncTrackingFilesAsync(cancellationToken);
        }

        public async Task<bool> EnableAsync(string filePath)
        {
            return await manager.EnableAsync(filePath, _cts.Token);
        }

        public async Task WaitForInitialScanCompletionAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken == default)
            {
                await _initialScanCompletionSource.Task;
                return;
            }

            var tcs = new TaskCompletionSource<bool>();
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

            await Task.WhenAny(_initialScanCompletionSource.Task, tcs.Task);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Waiting for initial scan completion was cancelled.", cancellationToken);
            }

            await _initialScanCompletionSource.Task; // Ensure the task is truly completed
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts.Cancel();
                manager.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            _cts.Cancel();
            await manager.DisposeAsync();
        }
    }
}