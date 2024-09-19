using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using System.Collections.Concurrent;

namespace FileMoles.Tracking;

internal class TrackingManager(DbContext dbContext, int debounceTime) : IDisposable
{
    private readonly DbContext _dbContext = dbContext;
    private readonly ConcurrentDictionary<string, TrackingDirectoryManager> _trackingDirs = [];
    private readonly Debouncer<string, FileSystemEvent> _debouncer = new(TimeSpan.FromMilliseconds(debounceTime));

    internal event EventHandler<FileContentChangedEventArgs>? FileContentChanged;

    public async Task InitializeAsync()
    {
        await LoadTrackingDirsAsync();
    }

    private async Task LoadTrackingDirsAsync()
    {
        var dirs = await _dbContext.TrackingDirs.GetAllAsync();
        foreach (var dir in dirs)
        {
            var manager = await TrackingDirectoryManager.CreateByDirectoryAsync(dir.Path, _dbContext);
            _trackingDirs.TryAdd(dir.Path, manager);
        }
    }

    public async Task TrackingAsync(string path)
    {
        if (_trackingDirs.ContainsKey(path))
            return;

        if (Directory.Exists(path))
        {
            var manager = await TrackingDirectoryManager.CreateByDirectoryAsync(path, _dbContext);
            _trackingDirs.TryAdd(path, manager);
        }
        else if (File.Exists(path))
        {
            var dirPath = Path.GetDirectoryName(path)!;
            var manager = await TrackingDirectoryManager.CreateByFileAsync(path, _dbContext);
            _trackingDirs.TryAdd(dirPath, manager);
        }
        else
            throw new FileNotFoundException(path);
    }

    public async Task UntrackingAsync(string path)
    {
        if (_trackingDirs.TryRemove(path, out var manager))
        {
            await manager.UntrackingAsync();
        }
    }

    public Task<bool> IsTrackingFileAsync(string filePath)
    {
        var normalizedFilePath = Path.GetFullPath(filePath);
        foreach (var dirManager in _trackingDirs.Values)
        {
            var normalizedDirPath = Path.GetFullPath(dirManager.DirectoryPath);
            if (normalizedFilePath.StartsWith(normalizedDirPath) && !dirManager.IsIgnored(normalizedFilePath))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task HandleFileEventAsync(FileSystemEvent e)
    {
        foreach (var manager in _trackingDirs.Values)
        {
            if (e.FullPath.StartsWith(manager.DirectoryPath) && !manager.IsIgnored(e.FullPath))
            {
                await _debouncer.DebounceAsync(e.FullPath, e, ProcessDebouncedEventAsync);
                break;
            }
        }
    }

    private async Task ProcessDebouncedEventAsync(FileSystemEvent e)
    {
        if (_trackingDirs.TryGetValue(Path.GetDirectoryName(e.FullPath)!, out var manager))
        {
            if (manager.HasBackup(e.FullPath))
            {
                var strategy = DiffStrategyFactory.CreateStrategy(e.FullPath);
                var oldFilePath = manager.GetBackupFilePath(e.FullPath);
                var diff = await strategy.GenerateDiffAsync(oldFilePath, e.FullPath, CancellationToken.None);

                if (diff.IsChanged)
                {
                    FileContentChanged?.Invoke(this, new FileContentChangedEventArgs(
                        e.FullPath,
                        null,
                        e.ChangeType,
                        false,
                        diff));
                }
            }

            await manager.BackupFileAsync(e.FullPath);
        }
    }

    public void Dispose()
    {
        _debouncer.Dispose();
        _trackingDirs.Clear();
        GC.SuppressFinalize(this);
    }
}

