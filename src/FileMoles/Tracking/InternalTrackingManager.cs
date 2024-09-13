using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using FileMoles.Interfaces;
using FileMoles.Internal;
using System.Collections.Concurrent;

namespace FileMoles.Tracking;

internal class InternalTrackingManager : IDisposable, IAsyncDisposable
{
    private readonly TrackingConfigManager _configManager;
    private readonly EventDebouncer<FileSystemEvent> _fileEventDebouncer;
    private readonly EventHandler<FileContentChangedEventArgs> _fileContentChangedHandler;
    private readonly InMemoryFileTrackingStore _trackingStore;
    private readonly ConcurrentDictionary<string, bool> _trackedDirectories = new();
    private readonly IFileBackupManager _backupManager;
    private readonly CancellationTokenSource _cts = new();

    public InternalTrackingManager(
        int debounceTime,
        TrackingConfigManager configManager,
        EventHandler<FileContentChangedEventArgs> fileContentChangedHandler,
        IUnitOfWork unitOfWork,
        IFileBackupManager backupManager)
    {
        _configManager = configManager;
        _fileEventDebouncer = new EventDebouncer<FileSystemEvent>(debounceTime, OnDebouncedFileEvents);
        _fileContentChangedHandler = fileContentChangedHandler;
        _trackingStore = new InMemoryFileTrackingStore(unitOfWork);
        _backupManager = backupManager;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _trackingStore.InitializeAsync(cancellationToken);
    }

    public async Task SyncTrackingFilesAsync(CancellationToken cancellationToken)
    {
        var allTrackingFiles = _trackingStore.GetAllTrackingFiles();
        foreach (var trackingFile in allTrackingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (trackingFile.IsDirectory)
            {
                if (Directory.Exists(trackingFile.FullPath))
                {
                    foreach (var file in Directory.GetFiles(trackingFile.FullPath))
                    {
                        if (await ShouldTrackFileAsync(file, cancellationToken)
                            && !_trackingStore.TryGetTrackingFile(file, out _))
                        {
                            await EnableAsync(file, cancellationToken);
                        }
                    }
                }
            }
            else
            {
                await SyncTrackingFileAsync(trackingFile, cancellationToken);
            }
        }
    }

    private async Task SyncTrackingFileAsync(TrackingFile trackingFile, CancellationToken cancellationToken)
    {
        var filePath = trackingFile.FullPath;

        if (!File.Exists(filePath) && !Directory.Exists(filePath))
        {
            await RemoveTrackedFileAndBackupAsync(filePath, cancellationToken);
        }
        else if (!await _backupManager.BackupExistsAsync(filePath, cancellationToken) && File.Exists(filePath))
        {
            await TrackFileAsync(filePath, cancellationToken);
        }
        else if (await _backupManager.BackupExistsAsync(filePath, cancellationToken) && File.Exists(filePath))
        {
            if (await _backupManager.HasFileChangedAsync(filePath, cancellationToken))
            {
                await TrackFileAsync(filePath, cancellationToken);
            }
        }
    }

    private async Task<DiffResult?> TrackAndGetDiffAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!await ShouldTrackFileAsync(filePath, cancellationToken))
        {
            return null;
        }

        try
        {
            DiffResult diff;

            if (await _backupManager.BackupExistsAsync(filePath, cancellationToken))
            {
                var diffStrategy = DiffStrategyFactory.CreateStrategy(filePath);
                var backupPath = await _backupManager.GetBackupPathAsync(filePath, cancellationToken);
                diff = await diffStrategy.GenerateDiffAsync(backupPath, filePath, cancellationToken);
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
                            ModifiedText = await File.ReadAllTextAsync(filePath, cancellationToken)
                        }
                    ],
                    IsInitial = true
                };
            }

            if (diff.IsChanged || diff.IsInitial)
            {
                _ = TrackFileAsync(filePath, cancellationToken); // Fire and forget
            }

            return diff;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error tracking file: {filePath}. Error: {ex.Message}");
            throw new InvalidOperationException($"Failed to track and compare file: {filePath}", ex);
        }
    }

    private async Task TrackFileAsync(string filePath, CancellationToken cancellationToken)
    {
        await _backupManager.BackupFileAsync(filePath, cancellationToken);

        if (_trackingStore.TryGetTrackingFile(filePath, out var trackingFile) && trackingFile != null)
        {
            trackingFile.LastTrackedTime = DateTime.UtcNow;
            _trackingStore.AddOrUpdateTrackingFile(trackingFile);
        }
    }

    private async Task<bool> RemoveTrackedFileAndBackupAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_trackingStore.TryGetTrackingFile(filePath, out var _))
        {
            try
            {
                _trackingStore.RemoveTrackingFile(filePath);
                await _backupManager.DeleteBackupAsync(filePath, cancellationToken);
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

    private async Task OnDebouncedFileEvents(IEnumerable<FileSystemEvent> events)
    {
        foreach (var e in events)
        {
            await ProcessFileEventAsync(e);
        }
    }

    private async Task<bool> HasFileChangedAsync(string filePath, CancellationToken cancellationToken)
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

        return trackingFile == null || await _backupManager.HasFileChangedAsync(filePath, cancellationToken);
    }

    private TrackingFile? GetTrackingFile(string filePath)
    {
        _trackingStore.TryGetTrackingFile(filePath, out var trackingFile);
        return trackingFile;
    }

    private TrackingFile GetOrNewTrackingFile(string filePath)
    {
        return GetTrackingFile(filePath) ?? new TrackingFile
        {
            FullPath = filePath,
            Hash = TrackingFile.GeneratePathHash(filePath),
            IsDirectory = Directory.Exists(filePath),
            LastTrackedTime = DateTime.UtcNow
        };
    }

    private Task<bool> ShouldTrackFileAsync(string filePath, CancellationToken cancellationToken)
    {
        // 이미 추적대상인 파일
        if (_trackingStore.IsTrackingFile(filePath))
            return Task.FromResult(true);

        // 추적 폴더내 속한 파일
        if (File.Exists(filePath) &&
            _trackingStore.IsTrackingFile(Path.GetDirectoryName(filePath)!) &&
            _configManager.ShouldTrackFile(filePath))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public async Task<bool> EnableAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        var isDirectory = Directory.Exists(path);
        var trackingFile = new TrackingFile
        {
            FullPath = path,
            Hash = TrackingFile.GeneratePathHash(path),
            IsDirectory = isDirectory,
            LastTrackedTime = DateTime.UtcNow
        };
        _trackingStore.AddOrUpdateTrackingFile(trackingFile);

        if (isDirectory)
        {
            _trackedDirectories[path] = true;
            await InitializeTrackedFilesAsync(path, cancellationToken);
        }
        else
        {
            await TrackSingleFileAsync(path, cancellationToken);
        }

        return true;
    }

    private async Task InitializeTrackedFilesAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var files = await GetTrackedFilesAsync(directoryPath, cancellationToken);
        foreach (var file in files)
        {
            await TrackSingleFileAsync(file, cancellationToken);
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
                    await EnableAsync(e.FullPath, cancellationToken);
                }
                break;
            case WatcherChangeTypes.Deleted:
                _trackedDirectories.TryRemove(e.FullPath, out _);
                await DisableAsync(e.FullPath, cancellationToken);
                break;
            case WatcherChangeTypes.Renamed:
                if (e.OldFullPath != null)
                {
                    _trackedDirectories.TryRemove(e.OldFullPath, out _);
                    await DisableAsync(e.OldFullPath, cancellationToken);
                }
                if (IsInTrackedDirectory(e.FullPath))
                {
                    await EnableAsync(e.FullPath, cancellationToken);
                }
                break;
        }
    }

    private async Task HandleFileEventInternalAsync(FileSystemEvent e, CancellationToken cancellationToken)
    {
        if (await ShouldTrackFileAsync(e.FullPath, cancellationToken) || IsInTrackedDirectory(e.FullPath))
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
                    if (await HasFileChangedAsync(e.FullPath, _cts.Token))
                    {
                        var diff = await TrackAndGetDiffAsync(e.FullPath, _cts.Token);
                        if (diff != null && (diff.IsChanged || diff.IsInitial))
                        {
                            _fileContentChangedHandler.Invoke(
                                this,
                                e.CreateFileContentChangedEventArgs(diff));
                        }
                    }
                    break;
                case WatcherChangeTypes.Deleted:
                    await RemoveTrackedFileAsync(e.FullPath, _cts.Token);
                    break;
                case WatcherChangeTypes.Renamed:
                    if (e.OldFullPath != null)
                    {
                        await RemoveTrackedFileAsync(e.OldFullPath, _cts.Token);
                    }
                    if (await ShouldTrackFileAsync(e.FullPath, _cts.Token) || IsInTrackedDirectory(e.FullPath))
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

    private async Task TrackSingleFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (await ShouldTrackFileAsync(filePath, cancellationToken))
        {
            var trackingFile = GetOrNewTrackingFile(filePath);
            await TrackFileAsync(filePath, cancellationToken);
        }
    }

    public async Task<bool> DisableAsync(string path, CancellationToken cancellationToken)
    {
        if (_trackingStore.IsTrackingFile(path))
        {
            await DisableTrackingForDirectoryAsync(path, cancellationToken);
            return await RemoveTrackedFileAndBackupAsync(path, cancellationToken);
        }
        return false;
    }

    private async Task DisableTrackingForDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var trackedFiles = _trackingStore.GetTrackedFilesInDirectory(path);
        foreach (var file in trackedFiles)
        {
            await RemoveTrackedFileAndBackupAsync(file, cancellationToken);
        }
    }

    private async Task<List<string>> GetTrackedFilesAsync(string path, CancellationToken cancellationToken)
    {
        List<string> trackedFiles = [];

        if (File.Exists(path))
        {
            if (await ShouldTrackFileAsync(path, cancellationToken))
            {
                trackedFiles.Add(path);
            }
        }
        else if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (await ShouldTrackFileAsync(file, cancellationToken))
                {
                    trackedFiles.Add(file);
                }
            }
        }
        else
        {
            Logger.WriteLine($"Warning: Path does not exist: {path}");
        }

        return trackedFiles;
    }

    public bool IsTrackedFile(string filePath)
    {
        return _trackingStore.IsTrackingFile(filePath);
    }

    private async Task RemoveTrackedFileAsync(string filePath, CancellationToken cancellationToken)
    {
        _trackingStore.RemoveTrackingFile(filePath);
        await _backupManager.DeleteBackupAsync(filePath, cancellationToken);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _fileEventDebouncer.Dispose();
            _cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        _cts.Cancel();
        await _fileEventDebouncer.DisposeAsync();
        _cts.Dispose();
    }
}
