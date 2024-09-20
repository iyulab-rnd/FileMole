using FileMoles.Events;
using FileMoles.Indexing;
using FileMoles.Internal;
using FileMoles.Data;
using FileMoles.Interfaces;
using FileMoles.Tracking;
using System.Diagnostics;
using FileMoles.Monitoring;
using System.Runtime.CompilerServices;
using NPOI.SS.Formula.Eval;

namespace FileMoles;

public class FileMole : IDisposable
{
    private bool _disposed;

    private readonly FileMoleOptions _options;
    private readonly DbContext dbContext;
    private readonly FileIndexer _fileIndexer;
    private readonly MonitoringFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;
    private readonly TrackingManager _trackingManager;
    private readonly CancellationTokenSource _cts = new();
    private readonly InitialScanner _initialScanner;
    private readonly Task _initializationTask;

    // event for Monitoring
    public event EventHandler<FileMoleEventArgs>? FileCreated;
    public event EventHandler<FileMoleEventArgs>? FileChanged;
    public event EventHandler<FileMoleEventArgs>? FileDeleted;
    public event EventHandler<FileMoleEventArgs>? FileRenamed;

    public event EventHandler<FileMoleEventArgs>? DirectoryCreated;
    public event EventHandler<FileMoleEventArgs>? DirectoryChanged;
    public event EventHandler<FileMoleEventArgs>? DirectoryDeleted;
    public event EventHandler<FileMoleEventArgs>? DirectoryRenamed;

    // event for Tracking
    public event EventHandler<FileContentChangedEventArgs>? FileContentChanged;

    public bool IsInitialScanComplete { get; private set; }
    public event EventHandler? InitialScanCompleted;

    internal FileMole(FileMoleOptions options, DbContext dbContext)
    {
        _options = options;
        this.dbContext = dbContext;

        var dataPath = options.GetDataPath();
        var ignoreManager = new MonitoringFileIgnoreManager(dataPath);

        _fileIndexer = new FileIndexer(dbContext);
        _fileSystemWatcher = new MonitoringFileSystemWatcher(_fileIndexer, ignoreManager);
        _storageProviders = [];

        _trackingManager = new TrackingManager(dbContext, options.DebounceTime);
        _trackingManager.FileContentChanged += OnFileContentChanged;

        InitializeStorageProviders();
        InitializeFileWatcher();

        _initialScanner = new InitialScanner(dbContext, _fileIndexer, _storageProviders);

        _initializationTask = InitializeAsync(_cts.Token);
    }

    private void InitializeStorageProviders()
    {
        foreach (var mole in _options.Moles)
        {
            IStorageProvider provider = FileMoleHelper.CreateStorageProvider(mole.Type, mole.Provider);
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
            _ = _fileSystemWatcher.WatchDirectoryAsync(mole.Path);
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.OptimizeAsync(cancellationToken);

        // 비동기적으로 실행
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();

            _ = _trackingManager.InitializeAsync();

            await _initialScanner.ScanAsync(_options.Moles.Select(m => m.Path), cancellationToken);

            sw.Stop();
            Logger.Info($"Initial scan completed in {sw.Elapsed:G}");

            IsInitialScanComplete = true;
            InitialScanCompleted?.Invoke(this, EventArgs.Empty);
        }, cancellationToken);
    }

    private async void HandleDirectoryEvent(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
        if (e.FullPath.EndsWith(FileMoleGlobalOptions.HillName))
        {
            if (e.ChangeType == WatcherChangeTypes.Created)
            {
                var baseDir = Path.GetDirectoryName(e.FullPath)!;
                var r = this.IsTracking(baseDir);
                if (r == false)
                {
                    await this.TrackingAsync(baseDir);
                }
            }
            else if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                var baseDir = Path.GetDirectoryName(e.FullPath)!;
                var r = this.IsTracking(baseDir);
                if (r == true)
                {
                    await this.UntrackingAsync(baseDir);
                }
            }
        }
    }

    private async Task HandleFileEventAsync(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
        try
        {
            await _trackingManager.HandleFileEventAsync(e);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void RaiseDirectoryCreatedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        DirectoryCreated?.Invoke(this, args);
    }

    private void RaiseDirectoryChangedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        DirectoryChanged?.Invoke(this, args);
    }

    private void RaiseDirectoryDeletedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        DirectoryDeleted?.Invoke(this, args);
    }

    private void RaiseDirectoryRenamedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        DirectoryRenamed?.Invoke(this, args);
    }

    private void RaiseFileCreatedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        FileCreated?.Invoke(this, args);
    }

    private void RaiseFileChangedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        FileChanged?.Invoke(this, args);
    }

    private void RaiseFileDeletedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
        FileDeleted?.Invoke(this, args);
    }

    private void RaiseFileRenamedEvent(FileSystemEvent internalEvent)
    {
        var args = internalEvent.CreateFileMoleEventArgs();
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
            var file = await provider.GetFileAsync(filePath);
            _ = _fileIndexer.TryIndexFileAsync(file);
            return file;
        }
        else
        {
            throw new ArgumentException($"No storage provider found for filePath: {filePath}");
        }
    }

    public async IAsyncEnumerable<FileInfo> GetFilesAsync(string path)
    {
        var provider = GetStorageProviderForPath(path);
        if (provider != null)
        {
            await foreach (var file in provider.GetFilesAsync(path))
            {
                _ = _fileIndexer.TryIndexFileAsync(file);
                yield return file;
            }
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

    public IAsyncEnumerable<FileInfo> SearchFilesAsync(string search)
    {
        return _fileIndexer.SearchAsync(search);
    }

    public async Task AddMoleAsync(string path, MoleType type = MoleType.Local, string provider = "Default")
    {
        _options.Moles.Add(new Mole { Path = path, Type = type, Provider = provider });
        IStorageProvider storageProvider = FileMoleHelper.CreateStorageProvider(type, provider);
        _storageProviders[path] = storageProvider;
        await _fileSystemWatcher.WatchDirectoryAsync(path);
    }

    public async Task RemoveMoleAsync(string path)
    {
        var mole = _options.Moles.FirstOrDefault(p => p.Path == path);
        if (mole != null)
        {
            _options.Moles.Remove(mole);
            _storageProviders.Remove(path);
            await _fileSystemWatcher.UnwatchDirectoryAsync(path);
        }
    }

    public IReadOnlyList<Mole> GetMoles()
    {
        return _options.Moles.AsReadOnly();
    }

    public async Task<long> GetTotalSizeAsync(string path)
    {
        return await _fileIndexer.GetTotalSizeAsync(path);
    }

    public async Task<int> GetFileCountAsync(string path)
    {
        return await _fileIndexer.GetFileCountAsync(path);
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

    public Task TrackingAsync(string fullPath)
    {
        return _trackingManager.TrackingAsync(fullPath);
    }

    public Task UntrackingAsync(string directory)
    {
        return _trackingManager.UntrackingAsync(directory);
    }

    public bool IsTracking(string filePath)
    {
        return _trackingManager.IsTracking(filePath);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
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
                    if (provider is IDisposable disposableProvider)
                    {
                        disposableProvider.Dispose();
                    }
                }
                _trackingManager.FileContentChanged -= OnFileContentChanged;
                _trackingManager.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}