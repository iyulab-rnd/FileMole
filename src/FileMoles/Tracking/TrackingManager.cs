using FileMoles.Events;

namespace FileMoles.Tracking;

public class TrackingManager : IDisposable, IAsyncDisposable
{
    private InternalTrackingManager manager = null!;
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource<bool> _isReady = new();

    public bool IsReady => _isReady.Task.IsCompleted;

    internal void Init(InternalTrackingManager manager)
    {
        this.manager = manager;
    }

    internal async Task HandleFileEventAsync(FileSystemEvent e, CancellationToken token)
    {
        await manager.HandleFileEventAsync(e, token);
    }

    internal async Task ReadyAsync(CancellationToken cancellationToken)
    {
        await manager.InitializeAsync(cancellationToken);
        await manager.SyncTrackingFilesAsync(cancellationToken);

        _isReady.TrySetResult(true);
    }

    public bool IsTrackedFile(string path)
    {
        return manager.IsTrackedFile(path);
    }

    public Task<bool> EnableAsync(string filePath)
    {
        return manager.EnableAsync(filePath, _cts.Token);
    }

    public Task<bool> DisableAsync(string path)
    {
        return manager.DisableAsync(path, _cts.Token);
    }

    public async Task WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken == default)
        {
            await _isReady.Task;
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        await Task.WhenAny(_isReady.Task, tcs.Task);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Waiting for initial scan completion was cancelled.", cancellationToken);
        }

        await _isReady.Task; // Ensure the task is truly completed
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