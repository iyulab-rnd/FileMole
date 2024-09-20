using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using FileMoles.Internal;
using System.Collections.Concurrent;
using System.Linq;

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
            await TrackingAsync(dir.Path);
        }
    }

    public async Task TrackingAsync(string path)
    {
        var normalizedPath = IOHelper.NormalizePath(path);

        if (Directory.Exists(normalizedPath))
        {
            var manager = await TrackingDirectoryManager.CreateByDirectoryAsync(normalizedPath, _dbContext);
            _trackingDirs.TryAdd(normalizedPath, manager);
        }
        else if (File.Exists(normalizedPath))
        {
            var dirPath = Path.GetDirectoryName(normalizedPath)!;

            if (_trackingDirs.TryGetValue(dirPath, out TrackingDirectoryManager? cachedManager))
            {
                await cachedManager.TrackingFileAsync(normalizedPath);
            }
            else
            {
                var manager = await TrackingDirectoryManager.CreateByFileAsync(normalizedPath, _dbContext);
                _trackingDirs.TryAdd(dirPath, manager);
            }
        }
    }

    public async Task UntrackingAsync(string path)
    {
        var normalizedPath = IOHelper.NormalizePath(path);

        if (Directory.Exists(normalizedPath))
        {
            if (_trackingDirs.TryRemove(normalizedPath, out var manager))
            {
                await manager.UntrackingAsync();
            }
            else
            {
                var hillPath = Path.Combine(normalizedPath, FileMoleGlobalOptions.HillName);
                if (Directory.Exists(hillPath))
                {
                    try
                    {
                        await RetryFile.DeleteAsync(hillPath);
                    }
                    catch (Exception)
                    {
                    }
                }

                var ignoreFilePath = Path.Combine(normalizedPath, FileMoleGlobalOptions.IgnoreFileName);
                if (File.Exists(ignoreFilePath))
                {
                    try
                    {
                        await RetryFile.DeleteAsync(ignoreFilePath);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        else if(File.Exists(normalizedPath))
        {
            var dir = Path.GetDirectoryName(normalizedPath)!;
            if (_trackingDirs.TryRemove(dir, out var manager))
            {
                await manager.UntrackingFileAsync(normalizedPath);
            }
            else
            {
            }
        }
    }

    public bool IsTracking(string filePath)
    {
        var normalizedPath = IOHelper.NormalizePath(filePath);
        if (Directory.Exists(normalizedPath))
        {
            return _trackingDirs.ContainsKey(normalizedPath);
        }
        else if (File.Exists(normalizedPath))
        {
            var dir = Path.GetDirectoryName(normalizedPath)!;
            var manager = _trackingDirs.GetValueOrDefault(dir);
            if (manager == null) return false;

            return manager.IsTrackingFile(normalizedPath);
        }
        else
            throw new FileNotFoundException(filePath);
    }

    internal async Task HandleFileEventAsync(FileSystemEvent e)
    {
        foreach (var manager in _trackingDirs.Values)
        {
            if (e.FullPath.StartsWith(manager.DirectoryPath) && !manager.IsIgnored(e.FullPath))
            {
                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    await manager.TrackingFileAsync(e.FullPath);
                    break;
                }
                else if (e.ChangeType == WatcherChangeTypes.Changed)
                {
                    await _debouncer.DebounceAsync(e.FullPath, e, ProcessDebouncedEventAsync);
                    break;
                }
                else if (e.ChangeType == WatcherChangeTypes.Renamed)
                {
                    if (e.OldFullPath != null)
                    {
                        await manager.UntrackingFileAsync(e.OldFullPath);
                    }
                    await manager.TrackingFileAsync(e.FullPath);
                    break;
                }
                else if (e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    await manager.UntrackingFileAsync(e.FullPath);
                    break;
                }
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

            await manager.TrackingFileAsync(e.FullPath);
        }
    }

    public void Dispose()
    {
        _debouncer.Dispose();
        _trackingDirs.Clear();
        GC.SuppressFinalize(this);
    }
}

