using FileMoles.Events;
using FileMoles.Indexing;
using FileMoles.Internal;
using FileMoles.Data;
using FileMoles.Interfaces;
using FileMoles.Tracking;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace FileMoles;

public class FileMole : IDisposable, IAsyncDisposable
{
    private bool _disposed;

    private readonly FileMoleOptions _options;
    private readonly IUnitOfWork _unitOfWork;
    private readonly FileIndexer _fileIndexer;
    private readonly FileMoleFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;
    private readonly CancellationTokenSource _cts = new();
    private readonly InitialScanner _initialScanner;
    private readonly Task _initializationTask;

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

    internal FileMole(FileMoleOptions options,
        IUnitOfWork unitOfWork,
        IFileBackupManager backupManager)
    {
        _options = options;
        _unitOfWork = unitOfWork;

        var dataPath = options.GetDataPath();
        var ignoreManager = new FileIgnoreManager(dataPath);

        _fileIndexer = new FileIndexer(unitOfWork);
        _fileSystemWatcher = new FileMoleFileSystemWatcher(_fileIndexer, ignoreManager);
        _storageProviders = [];

        Tracking = new TrackingManager();
        Tracking.Init(new InternalTrackingManager(
            ignoreManager,
            options.DebounceTime,
            OnFileContentChanged,
            unitOfWork,
            backupManager));

        InitializeStorageProviders();
        InitializeFileWatcher();

        _initialScanner = new InitialScanner(unitOfWork, _fileIndexer, _storageProviders);

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
        await _unitOfWork.OptimizeAsync(cancellationToken);

        // 비동기적으로 실행
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            _ = Tracking.ReadyAsync(cancellationToken);

            await _initialScanner.ScanAsync(_options.Moles.Select(m => m.Path), cancellationToken);

            sw.Stop();
            Logger.Info($"Initial scan completed in {sw.Elapsed:G}");

            IsInitialScanComplete = true;
            InitialScanCompleted?.Invoke(this, EventArgs.Empty);
        }, cancellationToken);
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
            // 객체가 이미 해제된 경우 무시
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
            await foreach(var file in provider.GetFilesAsync(path))
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

    public IAsyncEnumerable<FileInfo> SearchFilesAsync(string searchTerm)
    {
        return _fileIndexer.SearchAsync(searchTerm);
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
        long totalSize = 0;
        await foreach (var file in GetFilesAsync(path))
        {
            totalSize += file.Length;
        }
        return totalSize;
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
                Tracking.Dispose();
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_cts.IsCancellationRequested)
            return;

        _cts.Cancel();
        try
        {
            await _initializationTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected exception when task is canceled
        }

        await _fileSystemWatcher.DisposeAsync().ConfigureAwait(false);
        await Tracking.DisposeAsync().ConfigureAwait(false);
        _fileIndexer.Dispose();

        foreach (var provider in _storageProviders.Values)
        {
            if (provider is IAsyncDisposable asyncDisposableProvider)
            {
                await asyncDisposableProvider.DisposeAsync().ConfigureAwait(false);
            }
            else if (provider is IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }
        }
    }
}
