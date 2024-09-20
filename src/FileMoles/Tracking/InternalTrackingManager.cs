using System.Collections.Concurrent;
using FileMoles.Data;
using FileMoles.Diff;

namespace FileMoles.Tracking;

internal class InternalTrackingManager : IDisposable
{
    private readonly DbContext _dbContext;
    private readonly ConcurrentDictionary<string, bool> _trackingCache = new();
    private bool _isCacheRefreshed = false;

    public InternalTrackingManager(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    internal async Task<bool> IsTrackingAsync(string filePath)
    {
        if (_trackingCache.TryGetValue(filePath, out bool isTracking))
        {
            return isTracking;
        }

        if (_isCacheRefreshed)
        {
            return false;
        }
        else
        {
            return await IsTrackingFromDbAsync(filePath);
        }
    }

    private async Task<bool> IsTrackingFromDbAsync(string filePath)
    {
        bool isTracking = await _dbContext.TrackingFiles.IsTrackingFileAsync(filePath);
        if (isTracking)
        {
            _trackingCache[filePath] = true;
        }
        return isTracking;
    }

    internal Task TrackingAsync(string filePath)
    {
        if (File.Exists(filePath) != true)
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        _trackingCache[filePath] = true;
        _ = Task.Run(async () =>
        {
            var trackingFile = TrackingFile.CreateNew(filePath);
            await _dbContext.TrackingFiles.UpsertAsync(trackingFile);
            await BackupTryAsync(filePath);
        });
        return Task.CompletedTask;
    }

    internal Task UntrackingAsync(string filePath)
    {
        _trackingCache.TryRemove(filePath, out _);
        _ = Task.Run(async () =>
        {
            await _dbContext.TrackingFiles.DeleteAsync(filePath);
            await CleanupAsync(filePath);
        });
        return Task.CompletedTask;
    }

    internal async Task RefreshAsync()
    {
        _isCacheRefreshed = false;

        _trackingCache.Clear(); // Clear the cache before refreshing
        var trackingFiles = await _dbContext.TrackingFiles.FindAllAsync();

        foreach (var trackingFile in trackingFiles)
        {
            if (File.Exists(trackingFile.FullPath))
            {
                _trackingCache[trackingFile.FullPath] = true;
                _ = BackupTryAsync(trackingFile.FullPath);
            }
            else
            {
                _ = UntrackingAsync(trackingFile.FullPath);
            }
        }

        _isCacheRefreshed = true;
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

    private static Task CleanupAsync(string filePath)
    {
        return HillUtils.DeleteBackupAsync(filePath);
    }

    internal static Task<DiffResult?> GetDiffAsync(string fullPath)
    {
        return HillUtils.GetDiffAsync(fullPath);
    }
}