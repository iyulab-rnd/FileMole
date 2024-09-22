using System.Collections.Concurrent;
using FileMoles.Data;
using FileMoles.Diff;

namespace FileMoles.Tracking;

internal class InternalTrackingManager : IDisposable
{
    private readonly DbContext _dbContext;
    private readonly ConcurrentDictionary<string, bool> _trackingDirsCache = new();
    private readonly ConcurrentDictionary<string, bool> _trackingFilesCache = new();
    private bool _isCacheRefreshed = false;

    public InternalTrackingManager(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    internal Task<bool> IsTrackingAsync(string path)
    {
        if (Directory.Exists(path))
        {
            return IsTrackingDirAsync(path);
        }
        else
        {
            return IsTrackingFileAsync(path);
        }
    }

    private async Task<bool> IsTrackingDirAsync(string path)
    {
        if (_trackingDirsCache.TryGetValue(path, out bool isTracking))
        {
            return isTracking;
        }

        if (_isCacheRefreshed)
        {
            return false;
        }
        else
        {
            return await IsTrackingDirFromDbAsync(path);
        }
    }

    private async Task<bool> IsTrackingFileAsync(string filePath)
    {
        if (_trackingFilesCache.TryGetValue(filePath, out bool isTracking))
        {
            return isTracking;
        }

        if (_isCacheRefreshed)
        {
            return false;
        }
        else
        {
            return await IsTrackingFileFromDbAsync(filePath);
        }
    }

    private async Task<bool> IsTrackingDirFromDbAsync(string filePath)
    {
        bool isTracking = await _dbContext.TrackingDirs.IsTrackingDirAsync(filePath);
        if (isTracking)
        {
            _trackingDirsCache[filePath] = true;
        }
        return isTracking;
    }

    private async Task<bool> IsTrackingFileFromDbAsync(string filePath)
    {
        bool isTracking = await _dbContext.TrackingFiles.IsTrackingFileAsync(filePath);
        if (isTracking)
        {
            _trackingFilesCache[filePath] = true;
        }
        return isTracking;
    }

    internal Task TrackingAsync(string path)
    {
        return Directory.Exists(path) 
            ? TrackingDirAsync(path) 
            : TrackingFileAsync(path);
    }

    private async Task TrackingDirAsync(string path)
    {
        _trackingDirsCache[path] = true;

        var trackingDir = TrackingDir.CreateNew(path);
        await _dbContext.TrackingDirs.UpsertAsync(trackingDir);
    }

    private Task TrackingFileAsync(string filePath)
    {
        if (File.Exists(filePath) != true)
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        _trackingFilesCache[filePath] = true;
        _ = Task.Run(async () =>
        {
            var trackingFile = TrackingFile.CreateNew(filePath);
            await _dbContext.TrackingFiles.UpsertAsync(trackingFile);
            await BackupTryAsync(filePath);
        });
        return Task.CompletedTask;
    }

    internal Task UntrackingAsync(string path)
    {
        return Directory.Exists(path)
            ? UntrackingDirAsync(path)
            : UntrackingFileAsync(path);
    }

    private async Task UntrackingDirAsync(string path)
    {
        _trackingDirsCache.TryRemove(path, out _);
        await _dbContext.TrackingFiles.DeleteAsync(path);
    }

    private Task UntrackingFileAsync(string filePath)
    {
        _trackingFilesCache.TryRemove(filePath, out _);
        _ = Task.Run(async () =>
        {
            await _dbContext.TrackingFiles.DeleteAsync(filePath);
            await CleanupTrackingFileAsync(filePath);
        });
        return Task.CompletedTask;
    }

    internal async Task RefreshAsync()
    {
        _isCacheRefreshed = false;

        await RefreshTrackingDirsAsync();
        await RefreshTrackingFilesAsync();

        _isCacheRefreshed = true;
    }

    private async Task RefreshTrackingDirsAsync()
    {
        _trackingDirsCache.Clear();
        var trackingDirs = await _dbContext.TrackingDirs.FindAllAsync();

        foreach (var trackingDir in trackingDirs)
        {
            if (Directory.Exists(trackingDir.Path))
            {
                _trackingDirsCache[trackingDir.Path] = true;
            }
            else
            {
                _ = UntrackingDirAsync(trackingDir.Path);
            }
        }
    }

    private async Task RefreshTrackingFilesAsync()
    {
        _trackingFilesCache.Clear();
        var trackingFiles = await _dbContext.TrackingFiles.FindAllAsync();

        foreach (var trackingFile in trackingFiles)
        {
            if (File.Exists(trackingFile.FullPath))
            {
                _trackingFilesCache[trackingFile.FullPath] = true;
                _ = BackupTryAsync(trackingFile.FullPath);
            }
            else
            {
                _ = UntrackingAsync(trackingFile.FullPath);
            }
        }
    }

    internal static Task<bool> IsChangedAsync(string fullPath)
    {
        return HillUtils.IsChangedAsync(fullPath);
    }

    private static async Task BackupTryAsync(string fullPath)
    {
        var changed = await IsChangedAsync(fullPath);
        if (changed)
        {
            await HillUtils.BackupAsync(fullPath);
        }
    }

    private static Task CleanupTrackingFileAsync(string filePath)
    {
        return HillUtils.DeleteBackupAsync(filePath);
    }

    internal static Task<DiffResult?> GetDiffAsync(string fullPath)
    {
        return HillUtils.GetDiffAsync(fullPath);
    }
}