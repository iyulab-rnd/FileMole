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
    private readonly InMemoryTrackingStore _trackingStore;
    private bool _disposed = false;

    public TrackingManager(string basePath, double debounceTime, EventHandler<FileContentChangedEventArgs> fileContentChangedHandler)
    {
        _basePath = basePath;
        _configManager = new ConfigManager(Path.Combine(basePath, "config"));
        _fileEventDebouncer = new Debouncer<FileSystemEvent>(Convert.ToInt32(debounceTime), OnDebouncedFileEvents);
        _fileContentChangedHandler = fileContentChangedHandler;
        _dbContext = new DbContext(Path.Combine(basePath, "filemoles.db"));
        _hashGenerator = new HashGenerator();
        _trackingStore = new InMemoryTrackingStore(_dbContext);
        Directory.CreateDirectory(Path.Combine(_basePath, "backups"));
    }

    public async Task InitializeAsync()
    {
        await _trackingStore.InitializeAsync();
    }

    public Task HandleFileEventAsync(FileSystemEvent e)
    {
        return ShouldTrackFileAsync(e.FullPath).ContinueWith(shouldTrack =>
        {
            if (shouldTrack.Result)
            {
                return _fileEventDebouncer.DebounceAsync(e.FullPath, e);
            }
            return Task.CompletedTask;
        }, TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    private async Task OnDebouncedFileEvents(IEnumerable<FileSystemEvent> events)
    {
        foreach (var e in events)
        {
            if (_trackingStore.IsTrackingFile(e.FullPath))
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
                    RemoveTrackedFile(e.FullPath);
                    break;
                case WatcherChangeTypes.Renamed:
                    if (e.OldFullPath != null)
                    {
                        RemoveTrackedFile(e.OldFullPath);
                    }
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

        if (!_trackingStore.TryGetTrackingFile(filePath, out var trackingFile))
        {
            return true; // No tracking info exists, so it's a new file
        }

        // null 체크 추가
        if (trackingFile == null)
        {
            return true; // Tracking file is null, treat as a new file
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

    private async Task<DiffResult?> TrackAndGetDiffAsync(string filePath)
    {
        if (!await ShouldTrackFileAsync(filePath))
        {
            return null;
        }

        var trackingFile = GetTrackingFile(filePath);
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

            if (_trackingStore.TryGetTrackingFile(filePath, out var trackingFile) && trackingFile != null)
            {
                trackingFile.LastTrackedTime = DateTime.UtcNow;
                _trackingStore.AddOrUpdateTrackingFile(trackingFile);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void RemoveTrackedFile(string filePath)
    {
        if (_trackingStore.TryGetTrackingFile(filePath, out var trackingFile))
        {
            var backupPath = GetBackupPath(trackingFile!.BackupFileName);

            try
            {
                if (File.Exists(backupPath))
                {
                    Task.Run(() => FileSafe.DeleteRetryAsync(backupPath)).Forget();
                }

                _trackingStore.RemoveTrackingFile(filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove tracked file: {filePath}", ex);
            }
        }
    }

    private string GetBackupPath(string backupFileName)
    {
        return Path.Combine(_basePath, "backups", backupFileName);
    }

    private TrackingFile? GetTrackingFile(string filePath)
    {
        if (_trackingStore.TryGetTrackingFile(filePath, out var trackingFile))
        {
            return trackingFile!;
        }
        return null;
    }

    private TrackingFile GetOrNewTrackingFile(string filePath)
    {
        if (GetTrackingFile(filePath) is TrackingFile trackingFile)
        {
            return trackingFile;
        }
        else
        {
            return new TrackingFile(filePath, Directory.Exists(filePath));
        }
    }

    private Task<bool> ShouldTrackFileAsync(string filePath)
    {
        return Task.FromResult(_configManager.ShouldTrackFile(filePath) || _trackingStore.IsTrackingFile(filePath));
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

        _trackingStore.AddOrUpdateTrackingFile(trackingFile);
        return true;
    }

    private async Task TrackSingleFileAsync(string filePath)
    {
        if (await ShouldTrackFileAsync(filePath))
        {
            var trackingFile = GetOrNewTrackingFile(filePath);
            await TrackFileAsync(filePath, trackingFile.BackupFileName);
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

    public Task<bool> DisableAsync(string path)
    {
        if (_trackingStore.IsTrackingFile(path))
        {
            return Task.Run(async () =>
            {
                await DisableTrackingForDirectoryAsync(path);
                _trackingStore.RemoveTrackingFile(path);
                return true;
            });
        }
        else if (_trackingStore.IsTrackingFile(path))
        {
            RemoveTrackedFile(path);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    private Task DisableTrackingForDirectoryAsync(string path)
    {
        var trackedFiles = _trackingStore.GetTrackedFilesInDirectory(path);
        foreach (var file in trackedFiles)
        {
            RemoveTrackedFile(file);
        }
        return Task.CompletedTask;
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
        var allTrackingFiles = _trackingStore.GetAllTrackingFiles();
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
            RemoveTrackedFile(filePath);
        }
        else if (!File.Exists(backupPath) && File.Exists(filePath))
        {
            // 백업 파일이 없지만 실제 파일이 존재하는 경우
            await TrackFileAsync(filePath, trackingFile.BackupFileName);
        }
    }

    public bool IsTrackedFile(string filePath)
    {
        return _trackingStore.IsTrackingFile(filePath);
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