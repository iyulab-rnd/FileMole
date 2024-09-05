using System.Collections.Concurrent;
using FileMoles.Indexing;
using FileMoles.Events;

namespace FileMoles.Internals;

internal class FMFileSystemWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = [];
    private readonly IgnoreManager _ignoreManager;
    private readonly FileIndexer _fileIndexer;
    private readonly TimeSpan _debouncePeriod = TimeSpan.FromMilliseconds(400);
    private readonly ConcurrentDictionary<string, DateTime> _lastEventTime = new();
    private readonly ConcurrentDictionary<string, Task> _processingTasks = new();

    internal event EventHandler<FileSystemEvent>? FileCreated;
    internal event EventHandler<FileSystemEvent>? FileChanged;
    internal event EventHandler<FileSystemEvent>? FileDeleted;
    internal event EventHandler<FileSystemEvent>? FileRenamed;

    internal event EventHandler<FileSystemEvent>? DirectoryCreated;
    internal event EventHandler<FileSystemEvent>? DirectoryChanged;
    internal event EventHandler<FileSystemEvent>? DirectoryDeleted;
    internal event EventHandler<FileSystemEvent>? DirectoryRenamed;

    public FMFileSystemWatcher(FileMoleOptions options, FileIndexer fileIndexer)
    {
        _ignoreManager = new IgnoreManager(Path.Combine(options.GetDataPath(), ".ignore"));
        _fileIndexer = fileIndexer;
    }

    public Task WatchDirectoryAsync(string path)
    {
        if (_watchers.ContainsKey(path))
        {
            return Task.CompletedTask; // 이미 이 디렉토리를 감시하고 있음
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
        catch (Exception)
        {
            
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
        catch (Exception)
        {
            
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
        catch (Exception)
        {
            // Handle or log the exception
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
        catch (Exception)
        {
            // Handle or log the exception
        }
    }

    private async Task ProcessEventAsync(string fullPath, WatcherChangeTypes changeType, string? oldPath = null)
    {
        try
        {
            if (changeType == WatcherChangeTypes.Deleted)
            {
                await _fileIndexer.RemoveFileAsync(fullPath);
                FileDeleted?.Invoke(this, new FileSystemEvent(changeType, fullPath));
                return;
            }

            if (changeType == WatcherChangeTypes.Renamed && oldPath != null)
            {
                await _fileIndexer.RemoveFileAsync(oldPath);
                var info = new FileInfo(fullPath);
                if (info.Exists)
                {
                    await _fileIndexer.IndexFileAsync(info);
                    FileRenamed?.Invoke(this, new FileSystemEvent(changeType, fullPath, oldPath));
                }
                return;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Exists)
            {
                if (changeType == WatcherChangeTypes.Created || await _fileIndexer.HasFileChangedAsync(fileInfo))
                {
                    await _fileIndexer.IndexFileAsync(fileInfo);
                    if (changeType == WatcherChangeTypes.Created)
                    {
                        FileCreated?.Invoke(this, new FileSystemEvent(changeType, fullPath));
                    }
                    else
                    {
                        FileChanged?.Invoke(this, new FileSystemEvent(changeType, fullPath));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Handle or log the exception
        }
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