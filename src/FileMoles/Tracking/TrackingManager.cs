using FileMoles.Events;
using FileMoles.Indexing;
using FileMoles.Interfaces;
using System.IO;

namespace FileMoles.Tracking;

public class TrackingManager : IDisposable, IAsyncDisposable
{
    private InternalTrackingManager manager = null!;

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
    }

    internal async Task SyncTrackingFilesAsync(CancellationToken cancellationToken)
    {
        await manager.SyncTrackingFilesAsync(cancellationToken);
    }

    public async Task<bool> EnableAsync(string filePath)
    {
        return await manager.EnableAsync(filePath);
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
        await manager.DisposeAsync();
    }
}