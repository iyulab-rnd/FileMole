using System.Collections.Concurrent;
using FileMoles.Indexing;
using FileMoles.Events;

namespace FileMoles.Internal;

internal class FileMoleFileSystemWatcher : IDisposable, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly FileIgnoreManager _ignoreManager;
    private readonly FileIndexer _fileIndexer;
    private readonly TimeSpan _debouncePeriod = TimeSpan.FromMilliseconds(400);
    private readonly ConcurrentDictionary<string, DateTime> _lastEventTime = new();
    private readonly ConcurrentDictionary<string, Task> _processingTasks = new();
    private bool _disposed = false;

    internal event EventHandler<FileSystemEvent>? FileCreated;
    internal event EventHandler<FileSystemEvent>? FileChanged;
    internal event EventHandler<FileSystemEvent>? FileDeleted;
    internal event EventHandler<FileSystemEvent>? FileRenamed;

    internal event EventHandler<FileSystemEvent>? DirectoryCreated;
    internal event EventHandler<FileSystemEvent>? DirectoryChanged;
    internal event EventHandler<FileSystemEvent>? DirectoryDeleted;
    internal event EventHandler<FileSystemEvent>? DirectoryRenamed;

    public FileMoleFileSystemWatcher(
        FileIndexer fileIndexer,
        FileIgnoreManager ignoreManager)
    {
        _ignoreManager = ignoreManager;
        _fileIndexer = fileIndexer;
    }

    public Task WatchDirectoryAsync(string path)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileMoleFileSystemWatcher));
        }

        if (_watchers.ContainsKey(path))
        {
            return Task.CompletedTask;
        }

        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true
        };

        watcher.Created += OnCreated;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnDeleted;
        watcher.Renamed += OnRenamed;
        watcher.EnableRaisingEvents = true;
        _watchers[path] = watcher;

        return Task.CompletedTask;
    }

    private async void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (_ignoreManager.ShouldIgnore(e.FullPath)) return;

        try
        {
            if (Directory.Exists(e.FullPath))
            {
                DirectoryCreated?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Created, e.FullPath));
            }
            else
            {
                await ProcessEventAsync(e.FullPath, WatcherChangeTypes.Created);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error processing created event: {ex.Message}");
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_ignoreManager.ShouldIgnore(e.FullPath)) return;

        var now = DateTime.UtcNow;
        _lastEventTime[e.FullPath] = now;

        if (_processingTasks.TryGetValue(e.FullPath, out var existingTask) && !existingTask.IsCompleted)
        {
            _processingTasks[e.FullPath] = existingTask.ContinueWith(_ => DebouncedProcessChangeAsync(e.FullPath, now));
            return;
        }

        _processingTasks[e.FullPath] = DebouncedProcessChangeAsync(e.FullPath, now);
    }

    private async Task DebouncedProcessChangeAsync(string fullPath, DateTime eventTime)
    {
        await Task.Delay(_debouncePeriod);

        if (_lastEventTime.TryGetValue(fullPath, out var lastTime) && lastTime != eventTime) return;

        try
        {
            if (Directory.Exists(fullPath))
            {
                DirectoryChanged?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Changed, fullPath));
            }
            else
            {
                await ProcessEventAsync(fullPath, WatcherChangeTypes.Changed);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error processing changed event: {ex.Message}");
        }

        _processingTasks.TryRemove(fullPath, out _);
    }

    private async void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (_ignoreManager.ShouldIgnore(e.FullPath)) return;

        try
        {
            if (Directory.Exists(e.FullPath))
            {
                DirectoryDeleted?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Deleted, e.FullPath));
            }
            else
            {
                await ProcessEventAsync(e.FullPath, WatcherChangeTypes.Deleted);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error processing deleted event: {ex.Message}");
        }
    }

    private async void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (_ignoreManager.ShouldIgnore(e.FullPath) || _ignoreManager.ShouldIgnore(e.OldFullPath)) return;

        try
        {
            if (Directory.Exists(e.FullPath))
            {
                DirectoryRenamed?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath));
            }
            else
            {
                await ProcessEventAsync(e.FullPath, WatcherChangeTypes.Renamed, e.OldFullPath);
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error processing renamed event: {ex.Message}");
        }
    }

    private async Task ProcessEventAsync(string fullPath, WatcherChangeTypes changeType, string? oldPath = null)
    {
        try
        {
            if (changeType == WatcherChangeTypes.Deleted)
            {
                FileDeleted?.Invoke(this, new FileSystemEvent(changeType, fullPath));
                await _fileIndexer.RemoveFileAsync(fullPath);
                return;
            }

            if (changeType == WatcherChangeTypes.Renamed && oldPath != null)
            {
                var info = new FileInfo(fullPath);
                if (info.Exists)
                {
                    FileRenamed?.Invoke(this, new FileSystemEvent(changeType, fullPath, oldPath));

                    await _fileIndexer.RemoveFileAsync(oldPath);
                    await _fileIndexer.IndexFileAsync(info);
                }
                else
                {
                    await _fileIndexer.RemoveFileAsync(oldPath);
                }
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Exists)
            {
                if (changeType == WatcherChangeTypes.Created || await _fileIndexer.HasFileChangedAsync(fileInfo))
                {
                    if (changeType == WatcherChangeTypes.Created)
                    {
                        FileCreated?.Invoke(this, new FileSystemEvent(changeType, fullPath));
                    }
                    else
                    {
                        FileChanged?.Invoke(this, new FileSystemEvent(changeType, fullPath));
                    }
                    await _fileIndexer.IndexFileAsync(fileInfo);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error processing event: {ex.Message}");
        }
    }

    public Task UnwatchDirectoryAsync(string path)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        if (_watchers.TryRemove(path, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            UnsubscribeEvents(watcher);
            watcher.Dispose();
        }
        return Task.CompletedTask;
    }

    private void UnsubscribeEvents(FileSystemWatcher watcher)
    {
        watcher.Created -= OnCreated;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnDeleted;
        watcher.Renamed -= OnRenamed;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                UnsubscribeEvents(watcher);
                watcher.Dispose();
            }
            _watchers.Clear();

            // 이벤트 핸들러 해제
            FileCreated = null;
            FileChanged = null;
            FileDeleted = null;
            FileRenamed = null;
            DirectoryCreated = null;
            DirectoryChanged = null;
            DirectoryDeleted = null;
            DirectoryRenamed = null;
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisposeAsyncCore();

        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        foreach (var task in _processingTasks.Values)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                // 이미 종료된 태스크나 에러 발생 시 로깅
                Logger.WriteLine($"Error during task completion: {ex.Message}");
            }
        }

        foreach (var watcher in _watchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            UnsubscribeEvents(watcher);
            watcher.Dispose();
        }
        _watchers.Clear();

        // 이벤트 핸들러 해제
        FileCreated = null;
        FileChanged = null;
        FileDeleted = null;
        FileRenamed = null;
        DirectoryCreated = null;
        DirectoryChanged = null;
        DirectoryDeleted = null;
        DirectoryRenamed = null;
    }
}