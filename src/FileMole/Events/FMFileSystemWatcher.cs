using System.Collections.Concurrent;
using FileMole.Utils;
using FileMole.Indexing;
using FileMole.Storage;

namespace FileMole.Events;

public class FMFileSystemWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = [];
    private readonly Debouncer _debouncer;
    private readonly ConcurrentDictionary<string, FileSystemEvent> _pendingEvents = new();
    private readonly IgnoreManager _ignoreManager;
    private readonly FileIndexer _fileIndexer;
    private readonly HashGenerator _hashGenerator;

    public event EventHandler<FileSystemEvent>? FileSystemChanged;

    public FMFileSystemWatcher(TimeSpan debouncePeriod, FileIndexer fileIndexer)
    {
        _debouncer = new Debouncer(debouncePeriod);
        _ignoreManager = new IgnoreManager();
        _fileIndexer = fileIndexer;
        _hashGenerator = new HashGenerator();
    }

    public Task WatchDirectoryAsync(string path)
    {
        if (_watchers.ContainsKey(path))
        {
            return Task.CompletedTask; // Already watching this directory
        }

        var watcher = new FileSystemWatcher(path);
        watcher.IncludeSubdirectories = true;
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.EnableRaisingEvents = true;
        _watchers[path] = watcher;

        return Task.CompletedTask;
    }

    public Task UnwatchDirectoryAsync(string path)
    {
        if (_watchers.TryGetValue(path, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(path);
        }
        return Task.CompletedTask;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            var fmEvent = new FileSystemEvent(e.ChangeType, e.FullPath);
            _pendingEvents[e.FullPath] = fmEvent;
            _debouncer.DebounceAsync(e.FullPath, () => ProcessEventAsync(fmEvent)).Forget();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath) && !_ignoreManager.ShouldIgnore(e.OldFullPath))
        {
            var fmEvent = new FileSystemEvent(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
            _pendingEvents[e.FullPath] = fmEvent;
            _debouncer.DebounceAsync(e.FullPath, () => ProcessEventAsync(fmEvent)).Forget();
        }
    }

    private async Task ProcessEventAsync(FileSystemEvent e)
    {
        if (_pendingEvents.TryRemove(e.FullPath, out var _))
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                FileSystemChanged?.Invoke(this, e);
                await _fileIndexer.RemoveFileAsync(e.FullPath);
            }
            else
            {
                var fileInfo = new FileInfo(e.FullPath);
                var fmFileInfo = FMFileInfo.FromFileInfo(fileInfo);

                bool hasChanged = await _fileIndexer.HasFileChangedAsync(fmFileInfo);
                if (hasChanged)
                {
                    FileSystemChanged?.Invoke(this, e);
                    await _fileIndexer.IndexFileAsync(fmFileInfo);
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
        _watchers.Clear();

        GC.SuppressFinalize(this);
    }
}