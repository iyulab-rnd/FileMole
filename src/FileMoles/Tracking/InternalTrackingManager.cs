using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using FileMoles.Interfaces;
using FileMoles.Internal;

namespace FileMoles.Tracking;

internal class InternalTrackingManager : IDisposable, IAsyncDisposable
{
    private readonly EventDebouncer<FileSystemEvent> _fileEventDebouncer;
    private readonly EventHandler<FileContentChangedEventArgs> _fileContentChangedHandler;
    private readonly InMemoryFileTrackingStore _trackingStore;
    private readonly IFileBackupManager _backupManager;
    private readonly CancellationTokenSource _cts = new();

    public InternalTrackingManager(
        int debounceTime,
        EventHandler<FileContentChangedEventArgs> fileContentChangedHandler,
        IUnitOfWork unitOfWork,
        IFileBackupManager backupManager)
    {
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
            await SyncTrackingFileAsync(trackingFile, cancellationToken);
        }
    }

    private async Task SyncTrackingFileAsync(TrackingFile trackingFile, CancellationToken cancellationToken)
    {
        var filePath = trackingFile.FullPath;

        if (!File.Exists(filePath))
        {
            await RemoveTrackedFileAndBackupAsync(filePath, cancellationToken);
        }
        else if (!await _backupManager.BackupExistsAsync(filePath, cancellationToken))
        {
            await TrackFileAsync(filePath, cancellationToken);
        }
        else if (await _backupManager.HasFileChangedAsync(filePath, cancellationToken))
        {
            await TrackFileAsync(filePath, cancellationToken);
        }
    }

    private async Task<DiffResult?> TrackAndGetDiffAsync(string filePath, CancellationToken cancellationToken)
    {
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
            Logger.Error($"Error tracking file: {filePath}. Error: {ex.Message}");
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
        else
        {
            var newTrackingFile = TrackingFile.CreateNew(filePath);
            _trackingStore.AddOrUpdateTrackingFile(newTrackingFile);
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
                Logger.Error($"Error removing tracked file and backup: {filePath}. Error: {ex.Message}");
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

    public async Task<bool> EnableAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var trackingFile = new TrackingFile
        {
            FullPath = path,
            Hash = TrackingFile.GeneratePathHash(path),
            LastTrackedTime = DateTime.UtcNow
        };
        _trackingStore.AddOrUpdateTrackingFile(trackingFile);

        await TrackFileAsync(path, cancellationToken);

        return true;
    }

    internal async Task HandleFileEventAsync(FileSystemEvent e, CancellationToken cancellationToken)
    {
        if (_trackingStore.IsTrackingFile(e.FullPath))
        {
            await _fileEventDebouncer.DebounceAsync(e.FullPath, e);
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
                    if (_trackingStore.IsTrackingFile(e.FullPath))
                    {
                        await ProcessFileEventAsync(new FileSystemEvent(WatcherChangeTypes.Created, e.FullPath));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error processing file event for {e.FullPath}: {ex.Message}");
        }
    }

    public async Task<bool> DisableAsync(string path, CancellationToken cancellationToken)
    {
        if (_trackingStore.IsTrackingFile(path))
        {
            return await RemoveTrackedFileAndBackupAsync(path, cancellationToken);
        }
        return false;
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