using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FileMole.Utils;

namespace FileMole.Events;

public class FMFileSystemWatcher : IFMFileSystemWatcher
{
    private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
    private readonly Debouncer _debouncer;
    private readonly ConcurrentDictionary<string, FileSystemEvent> _pendingEvents = new ConcurrentDictionary<string, FileSystemEvent>();

    public event EventHandler<FileSystemEvent> FileSystemChanged;

    public FMFileSystemWatcher(TimeSpan debouncePeriod)
    {
        _debouncer = new Debouncer(debouncePeriod);
    }

    public Task WatchDirectoryAsync(string path)
    {
        var watcher = new FileSystemWatcher(path);
        watcher.IncludeSubdirectories = true;
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
        return Task.CompletedTask;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var fmEvent = new FileSystemEvent(e.ChangeType, e.FullPath);
        _pendingEvents[e.FullPath] = fmEvent;
        _debouncer.DebounceAsync(e.FullPath, () => RaiseEvent(fmEvent));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        var fmEvent = new FileSystemEvent(WatcherChangeTypes.Renamed, e.FullPath);
        _pendingEvents[e.FullPath] = fmEvent;
        _debouncer.DebounceAsync(e.FullPath, () => RaiseEvent(fmEvent));
    }

    private Task RaiseEvent(FileSystemEvent e)
    {
        if (_pendingEvents.TryRemove(e.FullPath, out var _))
        {
            FileSystemChanged?.Invoke(this, e);
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }
}