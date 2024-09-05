
using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using FileMoles.Internals;
using FileMoles.Utils;
using System.Collections.Concurrent;

namespace FileMoles;

public class TrackingManager : IDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private readonly ConfigManager _configManager;
    private readonly Debouncer<FileSystemEvent> _fileEventDebouncer;
    private readonly EventHandler<FileContentChangedEventArgs> _fileContentChangedHandler;
    private readonly DbContext _dbContext;
    private readonly HashGenerator _hashGenerator;
    private bool _disposed = false;

    public TrackingManager(string basePath, double debounceTime, EventHandler<FileContentChangedEventArgs> fileContentChangedHandler)
    {
        _basePath = basePath;
        _configManager = new ConfigManager(Path.Combine(basePath, "config"));
        _fileEventDebouncer = new Debouncer<FileSystemEvent>(Convert.ToInt32(debounceTime), OnDebouncedFileEvents);
        _fileContentChangedHandler = fileContentChangedHandler;
        _dbContext = new DbContext(Path.Combine(basePath, "filemoles.db"));
        _hashGenerator = new HashGenerator();
        Directory.CreateDirectory(Path.Combine(_basePath, "backups"));
    }

    public async Task HandleFileEventAsync(FileSystemEvent e)
    {
        if (await ShouldTrackFileAsync(e.FullPath))
        {
            await _fileEventDebouncer.DebounceAsync(e.FullPath, e);
        }
    }

    private async Task OnDebouncedFileEvents(IEnumerable<FileSystemEvent> events)
    {
        foreach (var e in events)
        {
            if (await _dbContext.TrackingFiles.IsTrackingFileAsync(e.FullPath))
            {
                await ProcessFileEventAsync(e);
            }
        }
    }

    private async Task ProcessFileEventAsync(FileSystemEvent e)
    {
        try
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                case WatcherChangeTypes.Changed:
                    if (await HasFileChangedAsync(e.FullPath))
                    {
                        var diff = await TrackAndGetDiffAsync(e.FullPath);
                        if (diff != null && (diff.IsChanged || diff.IsInitial))
                        {
                            _fileContentChangedHandler.Invoke(this, new FileContentChangedEventArgs(e, diff));
                        }
                    }
                    break;
                case WatcherChangeTypes.Deleted:
                    await RemoveTrackedFileAsync(e.FullPath);
                    break;
                case WatcherChangeTypes.Renamed:
                    await RemoveTrackedFileAsync(e.OldFullPath!);
                    if (await ShouldTrackFileAsync(e.FullPath))
                    {
                        await ProcessFileEventAsync(new FileSystemEvent(WatcherChangeTypes.Created, e.FullPath));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing file event for {e.FullPath}: {ex.Message}");
        }
    }

    private async Task<bool> HasFileChangedAsync(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return true; // File has been deleted
        }

        var trackingFile = await _dbContext.TrackingFiles.GetTrackingFileAsync(filePath);
        if (trackingFile == null)
        {
            return true; // No tracking info exists, so it's a new file
        }

        var backupPath = GetBackupPath(trackingFile.BackupFileName);
        var backupInfo = new FileInfo(backupPath);

        if (!backupInfo.Exists)
        {
            return true; // No backup exists, so it's a new file
        }

        if (fileInfo.Length != backupInfo.Length || fileInfo.LastWriteTimeUtc != backupInfo.LastWriteTimeUtc)
        {
            // If file size or last modified time has changed, calculate and compare hashes
            var currentHash = await _hashGenerator.GenerateHashAsync(filePath);
            var backupHash = await _hashGenerator.GenerateHashAsync(backupPath);

            return currentHash != backupHash;
        }

        return false; // File hasn't changed
    }

    private async Task RemoveTrackedFileAsync(string filePath)
    {
        var trackingFile = await _dbContext.TrackingFiles.GetTrackingFileAsync(filePath);
        if (trackingFile != null)
        {
            var backupPath = GetBackupPath(trackingFile.BackupFileName);

            try
            {
                if (File.Exists(backupPath))
                {
                    await FileSafe.DeleteRetryAsync(backupPath);
                }

                await _dbContext.TrackingFiles.RemoveTrackingFileAsync(filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove tracked file: {filePath}", ex);
            }
        }
    }

    private async Task<DiffResult?> TrackAndGetDiffAsync(string filePath)
    {
        if (!await ShouldTrackFileAsync(filePath))
        {
            return null;
        }

        var trackingFile = await GetOrCreateTrackingFileAsync(filePath);
        if (trackingFile == null)
        {
            return null;
        }

        var backupPath = GetBackupPath(trackingFile.BackupFileName);

        try
        {
            DiffResult diff;

            if (File.Exists(backupPath))
            {
                var diffStrategy = DiffStrategyFactory.CreateStrategy(filePath);
                diff = await diffStrategy.GenerateDiffAsync(backupPath, filePath);
            }
            else
            {
                diff = new TextDiffResult
                {
                    FileType = "Text",
                    Entries =
                    [
                        new TextDiffEntry
                        {
                            Type = DiffType.Inserted,
                            ModifiedText = await File.ReadAllTextAsync(filePath)
                        }
                    ],
                    IsInitial = true
                };
            }

            if (diff.IsChanged || diff.IsInitial)
            {
                await TrackFileAsync(filePath, trackingFile.BackupFileName);
            }

            return diff;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error tracking file: {filePath}. Error: {ex.Message}");
            throw new InvalidOperationException($"Failed to track and compare file: {filePath}", ex);
        }
    }

    private async Task TrackFileAsync(string filePath, string backupFileName)
    {
        var backupPath = GetBackupPath(backupFileName);
        var semaphore = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var backupDirectory = Path.GetDirectoryName(backupPath);
            if (backupDirectory != null)
            {
                Directory.CreateDirectory(backupDirectory);
            }
            await FileMoleUtils.CopyFileAsync(filePath, backupPath);

            await _dbContext.TrackingFiles.UpdateLastTrackedTimeAsync(filePath, DateTime.UtcNow);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private string GetBackupPath(string backupFileName)
    {
        return Path.Combine(_basePath, "backups", backupFileName);
    }

    private async Task<TrackingFile?> GetOrCreateTrackingFileAsync(string filePath)
    {
        var trackingFile = await _dbContext.TrackingFiles.GetTrackingFileAsync(filePath);
        if (trackingFile == null)
        {
            trackingFile = new TrackingFile(filePath, Directory.Exists(filePath));
            await _dbContext.TrackingFiles.AddTrackingFileAsync(trackingFile);
        }
        return trackingFile;
    }

    private async Task<bool> ShouldTrackFileAsync(string filePath)
    {
        return _configManager.ShouldTrackFile(filePath) || await _dbContext.TrackingFiles.IsTrackingFileAsync(filePath);
    }

    public async Task<bool> EnableAsync(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        var isDirectory = Directory.Exists(path);
        var trackingFile = new TrackingFile(path, isDirectory);

        await Task.Run(async () =>
        {
            if (isDirectory)
            {
                await InitializeTrackedFilesAsync(path);
            }
            else
            {
                await TrackSingleFileAsync(path);
            }
        });

        await _dbContext.TrackingFiles.AddTrackingFileAsync(trackingFile);
        return true;
    }

    private async Task TrackSingleFileAsync(string filePath)
    {
        if (await ShouldTrackFileAsync(filePath))
        {
            var trackingFile = await GetOrCreateTrackingFileAsync(filePath);
            if (trackingFile != null)
            {
                await TrackFileAsync(filePath, trackingFile.BackupFileName);
            }
        }
    }

    private async Task InitializeTrackedFilesAsync(string directoryPath)
    {
        var files = await GetTrackedFilesAsync(directoryPath);
        foreach (var file in files)
        {
            await TrackSingleFileAsync(file);
        }
    }

    public async Task<bool> DisableAsync(string path)
    {
        if (await _dbContext.TrackingFiles.IsTrackingFileAsync(path))
        {
            await DisableTrackingForDirectoryAsync(path);
            await _dbContext.TrackingFiles.RemoveTrackingFileAsync(path);
            return true;
        }
        else if (await _dbContext.TrackingFiles.IsTrackingFileAsync(path))
        {
            await RemoveTrackedFileAsync(path);
            return true;
        }
        return false;
    }

    private async Task DisableTrackingForDirectoryAsync(string path)
    {
        var trackedFiles = await _dbContext.TrackingFiles.GetTrackedFilesInDirectoryAsync(path);
        foreach (var file in trackedFiles)
        {
            await RemoveTrackedFileAsync(file);
        }
    }

    private async Task<List<string>> GetTrackedFilesAsync(string path)
    {
        List<string> trackedFiles = [];

        await Task.Run(async () =>
        {
            if (File.Exists(path))
            {
                if (await ShouldTrackFileAsync(path))
                {
                    trackedFiles.Add(path);
                }
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    if (await ShouldTrackFileAsync(file))
                    {
                        trackedFiles.Add(file);
                    }
                }
            }
            else
            {
                Console.WriteLine($"Warning: Path does not exist: {path}");
            }
        });

        return trackedFiles;
    }

    internal async Task SyncTrackingFilesAsync()
    {
        var allTrackingFiles = await _dbContext.TrackingFiles.GetAllTrackingFilesAsync();
        foreach (var trackingFile in allTrackingFiles)
        {
            await SyncTrackingFileAsync(trackingFile);
        }
    }

    private async Task SyncTrackingFileAsync(TrackingFile trackingFile)
    {
        var filePath = trackingFile.FullPath;
        var backupPath = GetBackupPath(trackingFile.BackupFileName);

        if (!File.Exists(filePath) && !Directory.Exists(filePath))
        {
            // 실제 파일/디렉토리가 없는 경우
            await RemoveTrackedFileAsync(filePath);
        }
        else if (!File.Exists(backupPath) && File.Exists(filePath))
        {
            // 백업 파일이 없지만 실제 파일이 존재하는 경우
            await TrackFileAsync(filePath, trackingFile.BackupFileName);
        }
    }

    public async Task<bool> IsTrackedFileAsync(string filePath)
    {
        return await _dbContext.TrackingFiles.IsTrackingFileAsync(filePath);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fileEventDebouncer.Dispose();
                _dbContext.Dispose();
                foreach (var semaphore in _fileLocks.Values)
                {
                    semaphore.Dispose();
                }
            }

            _disposed = true;
        }
    }
}