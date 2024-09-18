using FileMoles.Data;
using FileMoles.Diff;
using FileMoles.Events;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace FileMoles.Tracking;

internal class TrackingManager : IDisposable
{
    private readonly DbContext _dbContext;
    private readonly int _debounceTime;
    private readonly ConcurrentDictionary<string, TrackingDirectoryManager> _trackingDirs = [];
    private readonly Channel<FileSystemEvent> _eventChannel;
    private readonly CancellationTokenSource _cts = new();

    internal event EventHandler<FileContentChangedEventArgs>? FileContentChanged;

    public TrackingManager(DbContext dbContext, int debounceTime)
    {
        _dbContext = dbContext;
        _debounceTime = debounceTime;
        _eventChannel = Channel.CreateUnbounded<FileSystemEvent>();
        StartEventProcessing(_cts.Token);
    }

    public async Task InitializeAsync()
    {
        await LoadTrackingDirsAsync();
        await ReadyAsync();
    }

    private async Task LoadTrackingDirsAsync()
    {
        var dirs = await _dbContext.TrackingDirs.GetAllAsync();
        foreach (var dir in dirs)
        {
            var manager = new TrackingDirectoryManager(dir.Path, _dbContext);
            await manager.InitializeAsync();
            _trackingDirs.TryAdd(dir.Path, manager);
        }
    }

    private async Task ReadyAsync()
    {
        var dirsInDb = await _dbContext.TrackingDirs.GetAllAsync();
        foreach (var dir in dirsInDb)
        {
            if (!TrackingDirectoryManager.HasHill(dir.Path))
            {
                var manager = new TrackingDirectoryManager(dir.Path, _dbContext);
                await manager.InitializeAsync();

                _trackingDirs.TryAdd(dir.Path, new TrackingDirectoryManager(dir.Path, _dbContext));
            }
        }

        foreach (var dirManager in _trackingDirs.Values)
        {
            if (TrackingDirectoryManager.HasHill(dirManager.DirectoryPath))
            {
                var subDirs = Directory.GetDirectories(Path.Combine(dirManager.DirectoryPath, ".hill"));
                foreach (var subDir in subDirs)
                {
                    if (!await _dbContext.TrackingDirs.IsTrackingPathAsync(subDir))
                    {
                        var newDir = TrackingDir.CreateNew(subDir);
                        await _dbContext.TrackingDirs.UpsertAsync(newDir);

                        var manager = new TrackingDirectoryManager(subDir, _dbContext);
                        await manager.InitializeAsync();
                    }
                }
            }
        }

        await Task.CompletedTask;
    }

    public async Task TrackingAsync(string path)
    {
        if (_trackingDirs.ContainsKey(path))
            return;

        var manager = new TrackingDirectoryManager(path, _dbContext);
        await manager.InitializeAsync();
        _trackingDirs.TryAdd(path, manager);
    }

    public async Task UntrackingAsync(string path)
    {
        if (_trackingDirs.TryRemove(path, out var manager))
        {
            await manager.UntrackingAsync();
        }
    }

    public async Task<bool> IsTrackingFileAsync(string filePath)
    {
        var normalizedFilePath = Path.GetFullPath(filePath);
        foreach (var dirManager in _trackingDirs.Values)
        {
            var normalizedDirPath = Path.GetFullPath(dirManager.DirectoryPath);
            if (normalizedFilePath.StartsWith(normalizedDirPath) && !dirManager.IsIgnored(normalizedFilePath))
                return await Task.FromResult(true);
        }

        return await Task.FromResult(false);
    }

    internal async Task HandleFileEventAsync(FileSystemEvent e, CancellationToken token)
    {
        foreach (var manager in _trackingDirs.Values)
        {
            if (e.FullPath.StartsWith(manager.DirectoryPath) && !manager.IsIgnored(e.FullPath))
            {
                await _eventChannel.Writer.WriteAsync(e, token);
            }
        }
    }

    private async void StartEventProcessing(CancellationToken token)
    {
        var debounceDictionary = new ConcurrentDictionary<string, DateTime>();
        while (await _eventChannel.Reader.WaitToReadAsync(token))
        {
            if (_eventChannel.Reader.TryRead(out var fileEvent))
            {
                debounceDictionary[fileEvent.FullPath] = DateTime.UtcNow;

                _ = Task.Run(async () =>
                {
                    await Task.Delay(_debounceTime, token);

                    if (debounceDictionary.TryGetValue(fileEvent.FullPath, out var lastEventTime))
                    {
                        if ((DateTime.UtcNow - lastEventTime).TotalMilliseconds >= _debounceTime)
                        {
                            debounceDictionary.TryRemove(fileEvent.FullPath, out _);

                            if (_trackingDirs.TryGetValue(Path.GetDirectoryName(fileEvent.FullPath)!, out var manager))
                            {
                                if (manager.HasBackup(fileEvent.FullPath))
                                {
                                    var strategy = DiffStrategyFactory.CreateStrategy(fileEvent.FullPath);
                                    var oldFilePath = manager.GetBackupFilePath(fileEvent.FullPath);
                                    var diff = await strategy.GenerateDiffAsync(oldFilePath, fileEvent.FullPath, token);

                                    if (diff.IsChanged)
                                    {
                                        FileContentChanged?.Invoke(this, new FileContentChangedEventArgs(
                                            fileEvent.FullPath,
                                            null,
                                            fileEvent.ChangeType,
                                            false,
                                            diff));
                                    }
                                }

                                manager.BackupFile(fileEvent.FullPath);
                            }
                        }
                    }
                }, token);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        _trackingDirs.Clear();
        GC.SuppressFinalize(this);
    }
}
