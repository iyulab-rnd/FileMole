using FileMoles.Events;
using FileMoles.Internal;
using System.Collections.Concurrent;

namespace FileMoles.Tracking;

public class TrackingManager : IDisposable
{
    private InternalTrackingManager manager = null!;
    private readonly Debouncer<string, FileSystemEvent> debouncer;
    private readonly ConcurrentDictionary<string, FileSystemEvent> pendingEvents = new();

    public event EventHandler<FileContentChangedEventArgs>? FileContentChanged;

    public TrackingManager(int debounceTime)
    {
        debouncer = new Debouncer<string, FileSystemEvent>(TimeSpan.FromMilliseconds(debounceTime));
    }

    public void Dispose()
    {
        manager.Dispose();
        debouncer.Dispose();
        GC.SuppressFinalize(this);
    }

    internal async Task HandleFileEventAsync(FileSystemEvent e)
    {
        await debouncer.DebounceAsync(e.FullPath, e, async (latestEvent) =>
        {
            pendingEvents[latestEvent.FullPath] = latestEvent;
            await ProcessPendingEventsAsync();
        });
    }

    private async Task ProcessPendingEventsAsync()
    {
        foreach (var kvp in pendingEvents.ToArray())
        {
            if (pendingEvents.TryRemove(kvp.Key, out var e))
            {
                if (FileContentChanged != null)
                {
                    var changed = await InternalTrackingManager.IsChangedAsync(e.FullPath);
                    if (changed)
                    {
                        var diff = await InternalTrackingManager.GetDiffAsync(e.FullPath);
                        if (diff != null)
                        {
                            var args = e.CreateFileContentChangedEventArgs(diff);
                            FileContentChanged.Invoke(this, args);
                        }
                    }
                }
            }
        }
    }

    internal void Init(InternalTrackingManager internalTrackingManager)
    {
        manager = internalTrackingManager;
    }

    internal Task RefreshAsync() => manager.RefreshAsync();

    public Task<bool> IsTrackingAsync(string filePath) => manager.IsTrackingAsync(filePath);

    public Task TrackingAsync(string filePath) => manager.TrackingAsync(filePath);

    public Task UntrackingAsync(string filePath) => manager.UntrackingAsync(filePath);
}