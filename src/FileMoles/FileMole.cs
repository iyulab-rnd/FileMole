using FileMoles.Storage;
using FileMoles.Events;
using FileMoles.Indexing;
using FileMoles.Internals;
using System.Collections.Concurrent;

namespace FileMoles;

public class FileMole : IDisposable, IAsyncDisposable
{
    private readonly FileMoleOptions _options;
    private readonly FileIndexer _fileIndexer;
    private readonly FMFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _initializationTask;

    public ConfigManager Config { get; }
    public TrackingManager Tracking { get; }

    public event EventHandler<FileMoleEventArgs>? FileCreated;
    public event EventHandler<FileMoleEventArgs>? FileChanged;
    public event EventHandler<FileMoleEventArgs>? FileDeleted;
    public event EventHandler<FileMoleEventArgs>? FileRenamed;

    public event EventHandler<FileMoleEventArgs>? DirectoryCreated;
    public event EventHandler<FileMoleEventArgs>? DirectoryChanged;
    public event EventHandler<FileMoleEventArgs>? DirectoryDeleted;
    public event EventHandler<FileMoleEventArgs>? DirectoryRenamed;

    public event EventHandler<FileContentChangedEventArgs>? FileContentChanged;

    public bool IsInitialScanComplete { get; private set; }
    public event EventHandler? InitialScanCompleted;

    internal FileMole(FileMoleOptions options)
    {
        var dataPath = options.GetDataPath();

        _options = options;
        _fileIndexer = new FileIndexer(Path.Combine(dataPath, Constants.DbFileName));
        _fileSystemWatcher = new FMFileSystemWatcher(options, _fileIndexer);
        _storageProviders = [];

        Config = new ConfigManager(dataPath);
        Tracking = new TrackingManager(dataPath, options.DebounceTime, Config, OnFileContentChanged);

        InitializeStorageProviders();
        InitializeFileWatcher();

        _initializationTask = InitializeAsync(_cts.Token);
    }

    private void InitializeStorageProviders()
    {
        foreach (var mole in _options.Moles)
        {
            IStorageProvider provider = FileMoleUtils.CreateStorageProvider(mole.Type, mole.Provider);
            _storageProviders[mole.Path] = provider;
        }
    }

    private void InitializeFileWatcher()
    {
        _fileSystemWatcher.DirectoryCreated += (sender, e) => HandleDirectoryEvent(e, RaiseDirectoryCreatedEvent);
        _fileSystemWatcher.DirectoryChanged += (sender, e) => HandleDirectoryEvent(e, RaiseDirectoryChangedEvent);
        _fileSystemWatcher.DirectoryDeleted += (sender, e) => HandleDirectoryEvent(e, RaiseDirectoryDeletedEvent);
        _fileSystemWatcher.DirectoryRenamed += (sender, e) => HandleDirectoryEvent(e, RaiseDirectoryRenamedEvent);

        _fileSystemWatcher.FileCreated += async (sender, e) => await HandleFileEventAsync(e, RaiseFileCreatedEvent);
        _fileSystemWatcher.FileChanged += async (sender, e) => await HandleFileEventAsync(e, RaiseFileChangedEvent);
        _fileSystemWatcher.FileDeleted += async (sender, e) => await HandleFileEventAsync(e, RaiseFileDeletedEvent);
        _fileSystemWatcher.FileRenamed += async (sender, e) => await HandleFileEventAsync(e, RaiseFileRenamedEvent);

        foreach (var mole in _options.Moles)
        {
            _fileSystemWatcher.WatchDirectoryAsync(mole.Path);
        }
    }

    private Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            await Tracking.InitializeAsync(cancellationToken);
            await InitialScanAsync(cancellationToken);
            IsInitialScanComplete = true;
            InitialScanCompleted?.Invoke(this, EventArgs.Empty);
        }, cancellationToken);
    }

    private async Task InitialScanAsync(CancellationToken cancellationToken)
    {
        var scanTasks = _options.Moles.Select(mole => ScanDirectoryAsync(mole.Path, cancellationToken));
        await Task.WhenAll(scanTasks);
        await Tracking.SyncTrackingFilesAsync(cancellationToken);
    }

    private async Task ScanDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var storageProvider = GetStorageProviderForPath(path)
                ?? throw new ArgumentException($"No storage provider found for path: {path}");

            var batchSize = 100; // Adjust this value based on your system's capabilities
            var fileQueue = new ConcurrentQueue<FileInfo>();
            var indexingTasks = new List<Task>();

            async Task ProcessFileQueueAsync()
            {
                while (fileQueue.TryDequeue(out var file))
                {
                    await _fileIndexer.IndexFileAsync(file, cancellationToken);
                }
            }

            await foreach (var file in storageProvider.GetFilesAsync(path).WithCancellation(cancellationToken))
            {
                if (FileMoleUtils.IsHidden(file.FullName)) continue;

                fileQueue.Enqueue(file);

                if (fileQueue.Count >= batchSize)
                {
                    indexingTasks.Add(ProcessFileQueueAsync());
                }
            }

            indexingTasks.Add(ProcessFileQueueAsync()); // Process any remaining files

            await Task.WhenAll(indexingTasks);

            await foreach (var directory in storageProvider.GetDirectoriesAsync(path).WithCancellation(cancellationToken))
            {
                if (FileMoleUtils.IsHidden(directory.FullName)) continue;

                await ScanDirectoryAsync(directory.FullName, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error scanning directory {path}: {ex.Message}");
        }
    }

    private void HandleDirectoryEvent(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
    }

    private async Task HandleFileEventAsync(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
        try
        {
            await Tracking.HandleFileEventAsync(e, _cts.Token);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RaiseDirectoryCreatedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        DirectoryCreated?.Invoke(this, args);
    }

    private void RaiseDirectoryChangedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        DirectoryChanged?.Invoke(this, args);
    }

    private void RaiseDirectoryDeletedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        DirectoryDeleted?.Invoke(this, args);
    }

    private void RaiseDirectoryRenamedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        DirectoryRenamed?.Invoke(this, args);
    }

    private void RaiseFileCreatedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        FileCreated?.Invoke(this, args);
    }

    private void RaiseFileChangedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        FileChanged?.Invoke(this, args);
    }

    private void RaiseFileDeletedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        FileDeleted?.Invoke(this, args);
    }

    private void RaiseFileRenamedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        FileRenamed?.Invoke(this, args);
    }

    private void OnFileContentChanged(object? sender, FileContentChangedEventArgs e)
    {
        FileContentChanged?.Invoke(this, e);
    }

    public async Task<FileInfo> GetFileAsync(string filePath)
    {
        var provider = GetStorageProviderForPath(filePath);
        if (provider != null)
        {
            return await provider.GetFileAsync(filePath);
        }
        else
        {
            throw new ArgumentException($"No storage provider found for filePath: {filePath}");
        }
    }

    public IAsyncEnumerable<FileInfo> GetFilesAsync(string path)
    {
        var provider = GetStorageProviderForPath(path);
        if (provider != null)
        {
            return provider.GetFilesAsync(path);
        }
        else
        {
            throw new ArgumentException($"No storage provider found for path: {path}");
        }
    }

    public IAsyncEnumerable<DirectoryInfo> GetDirectoriesAsync(string path)
    {
        var provider = GetStorageProviderForPath(path);
        if (provider != null)
        {
            return provider.GetDirectoriesAsync(path);
        }
        else
        {
            throw new ArgumentException($"No storage provider found for path: {path}");
        }
    }

    public IAsyncEnumerable<FileInfo> SearchFilesAsync(string searchTerm)
    {
        return _fileIndexer.SearchAsync(searchTerm);
    }

    public async Task AddMoleAsync(string path, MoleType type = MoleType.Local, string provider = "Default")
    {
        _options.Moles.Add(new Mole { Path = path, Type = type, Provider = provider });
        IStorageProvider storageProvider = FileMoleUtils.CreateStorageProvider(type, provider);
        _storageProviders[path] = storageProvider;
        await _fileSystemWatcher.WatchDirectoryAsync(path);
    }

    public async Task RemoveMoleAsync(string path)
    {
        _options.Moles.RemoveAll(m => m.Path == path);
        _storageProviders.Remove(path);
        await _fileSystemWatcher.UnwatchDirectoryAsync(path);
    }

    public IReadOnlyList<Mole> GetMoles()
    {
        return _options.Moles.AsReadOnly();
    }

    public async Task<long> GetTotalSizeAsync(string path)
    {
        long totalSize = 0;
        await foreach(var file in GetFilesAsync(path))
        {
            totalSize += file.Length;
        }
        return totalSize;
    }

    public async Task<int> GetFileCountAsync(string path)
    {
        return await _fileIndexer.GetFileCountAsync(path);
    }

    public async Task ClearIndexAsync()
    {
        await _fileIndexer.ClearDatabaseAsync();
    }

    private IStorageProvider? GetStorageProviderForPath(string path)
    {
        return _storageProviders
            .Where(kvp => path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Key.Length)
            .Select(kvp => kvp.Value)
            .FirstOrDefault();
    }

    public Task MoveAsync(string fullPath, string destinationPath)
    {
        var fromStorage = GetStorageProviderForPath(fullPath);
        var toStorage = GetStorageProviderForPath(destinationPath);

        if (fromStorage == toStorage)
        {
            return fromStorage!.MoveAsync(fullPath, destinationPath);
        }
        else
        {
            throw new NotImplementedException("Moving files between different storage providers is not supported.");
        }
    }

    public Task CopyAsync(string fullPath, string destinationPath)
    {
        var fromStorage = GetStorageProviderForPath(fullPath);
        var toStorage = GetStorageProviderForPath(destinationPath);

        if (fromStorage == toStorage)
        {
            return fromStorage!.CopyAsync(fullPath, destinationPath);
        }
        else
        {
            throw new NotImplementedException("Copying files between different storage providers is not supported.");
        }
    }

    public Task RenameAsync(string fullPath, string newFileName)
    {
        var provider = GetStorageProviderForPath(fullPath);
        return provider!.RenameAsync(fullPath, newFileName);
    }

    public Task DeleteAsync(string fullPath)
    {
        var provider = GetStorageProviderForPath(fullPath);
        return provider!.DeleteAsync(fullPath);
    }

    #region Dispose

    private bool _disposed = false;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _disposeLock.Wait();
            try
            {
                _cts.Cancel();
                try
                {
                    _initializationTask.Wait();
                }
                catch (AggregateException ae)
                {
                    if (ae.InnerException is not OperationCanceledException)
                    {
                        throw;
                    }
                }
                _cts.Dispose();
                _fileSystemWatcher.Dispose();
                _fileIndexer.Dispose();
                foreach (var provider in _storageProviders.Values)
                {
                    provider.Dispose();
                }
                Tracking.Dispose();

                // DbContext 명시적 정리
                Resolver.DbContext?.Dispose();
            }
            finally
            {
                _disposeLock.Release();
            }
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeLock.WaitAsync();
        try
        {
            if (_disposed)
                return;

            _cts.Cancel();
            try
            {
                await _initializationTask;
            }
            catch (OperationCanceledException)
            {
                // 예상된 예외이므로 무시
            }

            _cts.Dispose();
            _fileSystemWatcher.Dispose();
            await _fileIndexer.DisposeAsync();
            foreach (var provider in _storageProviders.Values)
            {
                provider.Dispose();
            }
            Tracking.Dispose();

            // DbContext 명시적 정리
            if (Resolver.DbContext != null)
            {
                await Resolver.DbContext.DisposeAsync();
            }

            _disposed = true;
        }
        finally
        {
            _disposeLock.Release();
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}