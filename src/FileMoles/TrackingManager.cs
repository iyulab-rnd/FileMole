using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using FileMoles.Internals;
using FileMoles.Utils;
using NPOI.SS.Formula.Functions;
using System.Collections.Concurrent;

namespace FileMoles;

public class TrackingManager : IDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private readonly ConfigManager _configManager;
    private readonly Debouncer<FileSystemEvent> _fileEventDebouncer;
    private readonly EventHandler<FileContentChangedEventArgs> _fileContentChangedHandler;
    private readonly HashGenerator _hashGenerator;
    private readonly InMemoryTrackingStore _trackingStore;
    private readonly ConcurrentDictionary<string, bool> _trackedDirectories = new();

    public TrackingManager(
        string basePath,
        double debounceTime,
        ConfigManager configManager,
        EventHandler<FileContentChangedEventArgs> fileContentChangedHandler)
    {
        _basePath = basePath;
        _configManager = configManager;
        _fileEventDebouncer = new Debouncer<FileSystemEvent>(Convert.ToInt32(debounceTime), OnDebouncedFileEvents);
        _fileContentChangedHandler = fileContentChangedHandler;
        _hashGenerator = new HashGenerator();
        _trackingStore = new InMemoryTrackingStore(Resolver.ResolveDbContext(Path.Combine(basePath, Constants.DbFileName)));
        Directory.CreateDirectory(Path.Combine(_basePath, Constants.BackupDirName));
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _trackingStore.InitializeAsync(cancellationToken);
    }

    internal async Task SyncTrackingFilesAsync(CancellationToken cancellationToken)
    {
        var allTrackingFiles = _trackingStore.GetAllTrackingFiles();
        foreach (var trackingFile in allTrackingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (trackingFile.IsDirectory)
            {
                if (Directory.Exists(trackingFile.FullPath))
                {
                    foreach(var file in Directory.GetFiles(trackingFile.FullPath))
                    {
                        // 추적폴더내 누락된 파일을 추가합니다.
                        if (await ShouldTrackFileAsync(file)
                            && _trackingStore.TryGetTrackingFile(file, out _) is false)
                        {
                            await EnableAsync(file);
                        }
                    }
                }
            }
            else
            {
                await SyncTrackingFileAsync(trackingFile, cancellationToken);
            }
        }

        await CleanupBackupFilesAsync(cancellationToken);
    }

    private async Task SyncTrackingFileAsync(TrackingFile trackingFile, CancellationToken cancellationToken)
    {
        var filePath = trackingFile.FullPath;
        var backupPath = GetBackupPath(trackingFile.GetBackupFileName());

        if (!File.Exists(filePath) && !Directory.Exists(filePath))
        {
            // 실제 파일/디렉토리가 없는 경우
            await RemoveTrackedFileAndBackupAsync(filePath);
        }
        else if (!File.Exists(backupPath) && File.Exists(filePath))
        {
            // 백업 파일이 없지만 실제 파일이 존재하는 경우
            await TrackFileAsync(filePath, trackingFile.GetBackupFileName());
        }
        else if (File.Exists(backupPath) && File.Exists(filePath))
        {
            // 백업 파일과 실제 파일이 모두 존재하는 경우, 해시 비교
            var currentHash = await _hashGenerator.GenerateHashAsync(filePath, cancellationToken);
            var backupHash = await _hashGenerator.GenerateHashAsync(backupPath, cancellationToken);

            if (currentHash != backupHash)
            {
                await TrackFileAsync(filePath, trackingFile.GetBackupFileName());
            }
        }
    }

    private async Task CleanupBackupFilesAsync(CancellationToken cancellationToken)
    {
        var backupDirectory = Path.Combine(_basePath, Constants.BackupDirName);
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        var backupFiles = Directory.GetFiles(backupDirectory, "*", SearchOption.AllDirectories);
        var trackedFiles = _trackingStore.GetAllTrackingFiles().ToDictionary(tf => tf.Hash, tf => tf);

        foreach (var backupFile in backupFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(backupFile);
            if (!trackedFiles.ContainsKey(fileName))
            {
                try
                {
                    await FileSafe.DeleteRetryAsync(backupFile, cancellationToken: cancellationToken);
                    Logger.WriteLine($"Removed unused backup file: {backupFile}");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Error removing unused backup file {backupFile}: {ex.Message}");
                }
            }
        }
    }

    private async Task OnDebouncedFileEvents(IEnumerable<FileSystemEvent> events)
    {
        foreach (var e in events.ToList())
        {
            await ProcessFileEventAsync(e);
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

        var backupPath = GetBackupPath(trackingFile.GetBackupFileName());
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

        var trackingFile = GetOrNewTrackingFile(filePath);
        var backupPath = GetBackupPath(trackingFile.GetBackupFileName());

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
                await TrackFileAsync(filePath, trackingFile.GetBackupFileName());
            }

            return diff;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error tracking file: {filePath}. Error: {ex.Message}");
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
            var backupDirectory = Path.GetDirectoryName(backupPath)!;
            if (!Directory.Exists(backupDirectory))
            {
                Directory.CreateDirectory(backupDirectory);
            }

            await FileSafe.CopyWithRetryAsync(filePath, backupPath);

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

    private string GetBackupPath(string backupFileName)
    {
        return Path.Combine(_basePath, Constants.BackupDirName, backupFileName);
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
        // 이미 추적대상인 파일
        if (_trackingStore.IsTrackingFile(filePath)) 
            return Task.FromResult(true);

        // 추적 폴더내 속한 파일
        else if (File.Exists(filePath) && 
            _trackingStore.IsTrackingFile(Path.GetDirectoryName(filePath)!) &&
            _configManager.ShouldTrackFile(filePath))
        {
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

    public async Task<bool> EnableAsync(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        var isDirectory = Directory.Exists(path);
        var trackingFile = new TrackingFile(path, isDirectory);
        _trackingStore.AddOrUpdateTrackingFile(trackingFile);

        await Task.Run(async () =>
        {
            if (isDirectory)
            {
                _trackedDirectories[path] = true;
                await InitializeTrackedFilesAsync(path);
            }
            else
            {
                await TrackSingleFileAsync(path);
            }
        });

        return true;
    }

    private async Task InitializeTrackedFilesAsync(string directoryPath)
    {
        var files = await GetTrackedFilesAsync(directoryPath);
        foreach (var file in files)
        {
            await TrackSingleFileAsync(file);
        }
    }

    internal async Task HandleFileEventAsync(FileSystemEvent e, CancellationToken cancellationToken)
    {
        if (e.IsDirectory)
        {
            await HandleDirectoryEventAsync(e, cancellationToken);
        }
        else
        {
            await HandleFileEventInternalAsync(e, cancellationToken);
        }
    }

    private async Task HandleDirectoryEventAsync(FileSystemEvent e, CancellationToken cancellationToken)
    {
        switch (e.ChangeType)
        {
            case WatcherChangeTypes.Created:
                if (IsInTrackedDirectory(e.FullPath))
                {
                    await EnableAsync(e.FullPath);
                }
                break;
            case WatcherChangeTypes.Deleted:
                _trackedDirectories.TryRemove(e.FullPath, out _);
                await DisableAsync(e.FullPath);
                break;
            case WatcherChangeTypes.Renamed:
                if (e.OldFullPath != null)
                {
                    _trackedDirectories.TryRemove(e.OldFullPath, out _);
                    await DisableAsync(e.OldFullPath);
                }
                if (IsInTrackedDirectory(e.FullPath))
                {
                    await EnableAsync(e.FullPath);
                }
                break;
        }
    }

    private async Task HandleFileEventInternalAsync(FileSystemEvent e, CancellationToken cancellationToken)
    {
        if (await ShouldTrackFileAsync(e.FullPath) || IsInTrackedDirectory(e.FullPath))
        {
            await _fileEventDebouncer.DebounceAsync(e.FullPath, e);
        }
    }

    private bool IsInTrackedDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(directory))
        {
            if (_trackedDirectories.ContainsKey(directory))
            {
                return true;
            }
            directory = Path.GetDirectoryName(directory);
        }
        return false;
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
                    if (await ShouldTrackFileAsync(e.FullPath) || IsInTrackedDirectory(e.FullPath))
                    {
                        await ProcessFileEventAsync(new FileSystemEvent(WatcherChangeTypes.Created, e.FullPath));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error processing file event for {e.FullPath}: {ex.Message}");
        }
    }

    private async Task TrackSingleFileAsync(string filePath)
    {
        if (await ShouldTrackFileAsync(filePath))
        {
            var trackingFile = GetOrNewTrackingFile(filePath);
            await TrackFileAsync(filePath, trackingFile.GetBackupFileName());
        }
    }

    public Task<bool> DisableAsync(string path)
    {
        if (_trackingStore.IsTrackingFile(path))
        {
            return Task.Run(async () =>
            {
                await DisableTrackingForDirectoryAsync(path);
                await RemoveTrackedFileAndBackupAsync(path);
                return true;
            });
        }
        else if (_trackingStore.IsTrackingFile(path))
        {
            return RemoveTrackedFileAndBackupAsync(path);
        }
        return Task.FromResult(false);
    }

    private async Task DisableTrackingForDirectoryAsync(string path)
    {
        var trackedFiles = _trackingStore.GetTrackedFilesInDirectory(path);
        foreach (var file in trackedFiles)
        {
            await RemoveTrackedFileAndBackupAsync(file);
        }
    }

    private async Task<bool> RemoveTrackedFileAndBackupAsync(string filePath)
    {
        if (_trackingStore.TryGetTrackingFile(filePath, out var trackingFile))
        {
            var backupPath = GetBackupPath(trackingFile!.GetBackupFileName());

            try
            {
                if (File.Exists(backupPath))
                {
                    await FileSafe.DeleteRetryAsync(backupPath);
                }

                _trackingStore.RemoveTrackingFile(filePath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Error removing tracked file and backup: {filePath}. Error: {ex.Message}");
                return false;
            }
        }
        return false;
    }

    private void RemoveTrackedFile(string filePath)
    {
        Task.Run(() => RemoveTrackedFileAndBackupAsync(filePath)).Forget();
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
                Logger.WriteLine($"Warning: Path does not exist: {path}");
            }
        });
        return trackedFiles;
    }

    public bool IsTrackedFile(string filePath)
    {
        return _trackingStore.IsTrackingFile(filePath);
    }

    private bool _disposed = false;

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
                foreach (var semaphore in _fileLocks.Values)
                {
                    semaphore.Dispose();
                }
            }

            _disposed = true;
        }
    }
}