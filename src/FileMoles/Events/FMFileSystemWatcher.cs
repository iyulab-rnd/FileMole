using System.Collections.Concurrent;
using FileMoles.Utils;
using FileMoles.Indexing;

namespace FileMoles.Events;

internal class FMFileSystemWatcher : IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = [];
    private readonly IgnoreManager _ignoreManager;
    private readonly FileIndexer _fileIndexer;
    private readonly TimeSpan _debouncePeriod = TimeSpan.FromMilliseconds(400);
    private readonly ConcurrentDictionary<string, DateTime> _lastEventTime = new();

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
        _ignoreManager = new IgnoreManager(Path.Combine(options.GetDataPath(), Constants.FileMoleIgnoreFile));
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

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            if (Directory.Exists(e.FullPath))
            {
                DirectoryCreated?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Created, e.FullPath));
            }
            else
            {
                ProcessCreatedEventAsync(e.FullPath).Forget();
            }
        }
    }

    private async void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            var now = DateTime.UtcNow;
            var lastEventTime = _lastEventTime.GetOrAdd(e.FullPath, now);

            if (now - lastEventTime < _debouncePeriod)
            {
                return;
            }

            _lastEventTime[e.FullPath] = now;

            if (Directory.Exists(e.FullPath))
            {
                DirectoryChanged?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Changed, e.FullPath));
            }
            else
            {
                var fileInfo = new FileInfo(e.FullPath);
                if (await _fileIndexer.HasFileChangedAsync(fileInfo))
                {
                    ProcessChangedEventAsync(e.FullPath).Forget();
                }
            }
        }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath))
        {
            if (e.FullPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                DirectoryDeleted?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Deleted, e.FullPath));
            }
            else
            {
                ProcessDeletedEventAsync(e.FullPath).Forget();
            }
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!_ignoreManager.ShouldIgnore(e.FullPath) && !_ignoreManager.ShouldIgnore(e.OldFullPath))
        {
            if (Directory.Exists(e.FullPath))
            {
                DirectoryRenamed?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath));
            }
            else
            {
                ProcessRenamedEventAsync(e.OldFullPath, e.FullPath).Forget();
            }
        }
    }

    private async Task ProcessCreatedEventAsync(string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Exists)
        {
            await _fileIndexer.IndexFileAsync(fileInfo);
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

        await _fileIndexer.IndexFileAsync(fileInfo);
        FileChanged?.Invoke(this, new FileSystemEvent(WatcherChangeTypes.Changed, fullPath));
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
            await _fileIndexer.IndexFileAsync(fileInfo);
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
