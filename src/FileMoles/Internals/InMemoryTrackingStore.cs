using System.Collections.Concurrent;
using FileMoles.Data;
using FileMoles.Utils;

namespace FileMoles.Internals;

internal class InMemoryTrackingStore(DbContext dbContext)
{
    private readonly ConcurrentDictionary<string, TrackingFile> _trackingFiles = new();
    private readonly DbContext _dbContext = dbContext;

    public async Task InitializeAsync()
    {
        var allTrackingFiles = await _dbContext.TrackingFiles.GetAllTrackingFilesAsync();
        foreach (var file in allTrackingFiles)
        {
            _trackingFiles[file.FullPath] = file;
        }
    }

    public bool TryGetTrackingFile(string fullPath, out TrackingFile? trackingFile)
    {
        return _trackingFiles.TryGetValue(fullPath, out trackingFile);
    }

    public void AddOrUpdateTrackingFile(TrackingFile trackingFile)
    {
        _trackingFiles[trackingFile.FullPath] = trackingFile;
        Task.Run(() => _dbContext.TrackingFiles.AddTrackingFileAsync(trackingFile)).Forget();
    }

    public bool RemoveTrackingFile(string fullPath)
    {
        if (_trackingFiles.TryRemove(fullPath, out _))
        {
            Task.Run(() => _dbContext.TrackingFiles.RemoveTrackingFileAsync(fullPath)).Forget();
            return true;
        }
        return false;
    }

    public bool IsTrackingFile(string fullPath)
    {
        return _trackingFiles.ContainsKey(fullPath);
    }

    public IEnumerable<TrackingFile> GetAllTrackingFiles()
    {
        return _trackingFiles.Values;
    }

    public IEnumerable<string> GetTrackedFilesInDirectory(string directoryPath)
    {
        return _trackingFiles.Values
            .Where(tf => tf.FullPath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            .Select(tf => tf.FullPath);
    }
}