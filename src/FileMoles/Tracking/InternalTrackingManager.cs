using System.Collections.Concurrent;
using FileMoles.Data;
using FileMoles.Diff;

namespace FileMoles.Tracking;

internal class InternalTrackingManager : IDisposable
{
    private readonly DbContext _dbContext;
    private readonly StringComparer _pathComparer;
    private readonly ConcurrentDictionary<string, bool> _trackingDirsCache;
    private readonly ConcurrentDictionary<string, bool> _trackingFilesCache;
    private bool _isCacheRefreshed = false;

    public InternalTrackingManager(DbContext dbContext)
    {
        _dbContext = dbContext;
        _pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _trackingDirsCache = new ConcurrentDictionary<string, bool>(_pathComparer);
        _trackingFilesCache = new ConcurrentDictionary<string, bool>(_pathComparer);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    internal async Task<bool> IsTrackingAsync(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? await IsTrackingDirAsync(path)
                : await IsTrackingFileAsync(path);
        }
        catch (Exception)
        {
            return false;
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

    internal async Task<bool> TrackingAsync(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                await TrackingFileAsync(path);
            }
            else
            {
                await TrackingDirAsync(path);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task TrackingDirAsync(string path)
    {
        _trackingDirsCache[path] = true;

        var trackingDir = TrackingDir.CreateNew(path);
        await _dbContext.TrackingDirs.UpsertAsync(trackingDir);

        HillUtils.EnsureHillFolder(path);
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

    internal async Task<bool> UntrackingAsync(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                await UntrackingFileAsync(path);
            }
            else
            {
                await UntrackingDirAsync(path);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task UntrackingDirAsync(string path)
    {
        _trackingDirsCache.TryRemove(path, out _);
        await _dbContext.TrackingFiles.DeleteAsync(path);
        await CleanupDirAsync(path);
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

    internal async Task BackupTryAsync(string fullPath)
    {
        var changed = await IsChangedAsync(fullPath);
        if (changed)
        {
            await HillUtils.BackupAsync(fullPath);
        }
    }

    internal static Task<DiffResult?> GetDiffAsync(string fullPath)
    {
        return HillUtils.GetDiffAsync(fullPath);
    }

    private static async Task CleanupTrackingFileAsync(string filePath)
    {
        await HillUtils.DeleteBackupAsync(filePath);
        var dir = Path.GetDirectoryName(filePath)!;
        await CleanupDirAsync(dir);
    }

    private static async Task CleanupDirAsync(string dir)
    {
        await HillUtils.CleanupHillFolderAsync(dir);
    }
}