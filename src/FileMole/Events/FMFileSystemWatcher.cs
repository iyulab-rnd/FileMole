using System.Collections.Concurrent;
using FileMole.Utils;
using FileMole.Indexing;
using FileMole.Storage;

namespace FileMole.Events;

internal class FMFileSystemWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = [];
    private readonly Debouncer _debouncer;
    private readonly ConcurrentDictionary<string, FileSystemEvent> _pendingChangedEvents = new();
    private readonly IgnoreManager _ignoreManager;
    private readonly FileIndexer _fileIndexer;

    internal event EventHandler<FileSystemEvent>? FileCreated;
    internal event EventHandler<FileSystemEvent>? FileChanged;
    internal event EventHandler<FileSystemEvent>? FileDeleted;
    internal event EventHandler<FileSystemEvent>? FileRenamed;

    public FMFileSystemWatcher(TimeSpan debouncePeriod, FileIndexer fileIndexer)
    {
        _debouncer = new Debouncer(debouncePeriod);
        _ignoreManager = new IgnoreManager();
        _fileIndexer = fileIndexer;
    }

    public Task WatchDirectoryAsync(string path)
    {
        if (_watchers.ContainsKey(path))
        {
            return Task.CompletedTask; // Already watching this directory
        }

        var watcher = new FileSystemWatcher(path);
        watcher.IncludeSubdirectories = true;
        watcher.Created += OnCreated;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.EnableRaisingEvents = true;
        _watchers[path] = watcher;

        return Task.CompletedTask;
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            ProcessCreatedEventAsync(e.FullPath).Forget();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            _debouncer.DebounceAsync(e.FullPath, () => ProcessChangedEventAsync(e.FullPath)).Forget();
        }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            ProcessDeletedEventAsync(e.FullPath).Forget();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath) && !_ignoreManager.ShouldIgnore(e.OldFullPath))
        {
            ProcessRenamedEventAsync(e.OldFullPath, e.FullPath).Forget();
        }
    }

    private async Task ProcessCreatedEventAsync(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Exists)
        {
            var fmFileInfo = FMFileInfo.FromFileInfo(fileInfo);
            await _fileIndexer.IndexFileAsync(fmFileInfo);
            FileCreated?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Created, fullPath));
        }
    }

    private async Task ProcessChangedEventAsync(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            return; // 파일이 삭제되었거나 접근할 수 없는 경우
        }

        var fmFileInfo = FMFileInfo.FromFileInfo(fileInfo);
        if (await _fileIndexer.HasFileChangedAsync(fmFileInfo))
        {
            await _fileIndexer.IndexFileAsync(fmFileInfo);
            FileChanged?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Changed, fullPath));
        }
    }

    private async Task ProcessDeletedEventAsync(string fullPath)
    {
        await _fileIndexer.RemoveFileAsync(fullPath);
        FileDeleted?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Deleted, fullPath));
    }

    private async Task ProcessRenamedEventAsync(string oldPath, string newPath)
    {
        await _fileIndexer.RemoveFileAsync(oldPath);
        var fileInfo = new FileInfo(newPath);
        if (fileInfo.Exists)
        {
            var fmFileInfo = FMFileInfo.FromFileInfo(fileInfo);
            await _fileIndexer.IndexFileAsync(fmFileInfo);
        }
        FileRenamed?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Renamed, newPath, oldPath));
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