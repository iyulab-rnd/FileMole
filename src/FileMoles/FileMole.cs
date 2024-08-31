using FileMoles.Storage;
using FileMoles.Events;
using FileMoles.Indexing;

namespace FileMoles;

public class FileMole : IDisposable
{
    private readonly FileMoleOptions _options;
    private readonly FileIndexer _fileIndexer;
    private readonly FMFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;
    private readonly Debouncer<FileSystemEvent> _debouncer;

    public event EventHandler<FileMoleEventArgs>? FileCreated;
    public event EventHandler<FileMoleEventArgs>? FileChanged;
    public event EventHandler<FileMoleEventArgs>? FileDeleted;
    public event EventHandler<FileMoleEventArgs>? FileRenamed;
    public event EventHandler<FileMoleEventArgs>? DebouncedFileUpdated;

    public bool IsInitialScanComplete { get; private set; }
    public event EventHandler? InitialScanCompleted;

    public FileMole(FileMoleOptions options)
    {
        _options = options;
        _fileIndexer = new FileIndexer(options);
        _fileSystemWatcher = new FMFileSystemWatcher(_fileIndexer);
        _storageProviders = new Dictionary<string, IStorageProvider>();

        _debouncer = new Debouncer<FileSystemEvent>(_options.DebounceTime, OnDebouncedEvents);

        InitializeStorageProviders();
        InitializeFileWatcher();
    }

    private void InitializeStorageProviders()
    {
        foreach (var mole in _options.Moles)
        {
            IStorageProvider provider = CreateStorageProvider(mole.Type, mole.Provider);
            _storageProviders[mole.Path] = provider;
        }
    }

    private IStorageProvider CreateStorageProvider(MoleType type, string provider)
    {
        return (type, provider?.ToLower()) switch
        {
            (MoleType.Local, _) => new LocalStorageProvider(),
            (MoleType.Remote, _) => new RemoteStorageProvider(),
            (MoleType.Cloud, "onedrive") => new OneDriveStorageProvider(),
            (MoleType.Cloud, "google") => new GoogleDriveStorageProvider(),
            (MoleType.Cloud, _) => throw new NotSupportedException($"Unsupported cloud provider: {provider}"),
            _ => throw new NotSupportedException($"Unsupported storage type: {type}")
        };
    }

    private void InitializeFileWatcher()
    {
        _fileSystemWatcher.FileCreated += (sender, e) => HandleFileEvent(e, RaiseFileCreatedEvent);
        _fileSystemWatcher.FileChanged += (sender, e) => HandleFileEvent(e, RaiseFileChangedEvent);
        _fileSystemWatcher.FileDeleted += (sender, e) => HandleFileEvent(e, RaiseFileDeletedEvent);
        _fileSystemWatcher.FileRenamed += (sender, e) => HandleFileEvent(e, RaiseFileRenamedEvent);

        foreach (var mole in _options.Moles)
        {
            _fileSystemWatcher.WatchDirectoryAsync(mole.Path);
        }
    }

    private void HandleFileEvent(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
        _debouncer.Debounce(e.FullPath, e);
    }

    private void OnDebouncedEvents(IEnumerable<FileSystemEvent> events)
    {
        foreach (var e in events)
        {
            RaiseDebouncedFileUpdatedEvent(e);
        }
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

    private void RaiseDebouncedFileUpdatedEvent(FileSystemEvent internalEvent)
    {
        var args = new FileMoleEventArgs(internalEvent);
        DebouncedFileUpdated?.Invoke(this, args);
    }

    public async Task<FMFileInfo> GetFileAsync(string filePath)
    {
        if (_storageProviders.TryGetValue(filePath, out var provider))
        {
            return await provider.GetFileAsync(filePath);
        }
        else
        {
            var localProvider = new LocalStorageProvider();
            return await localProvider.GetFileAsync(filePath);
        }
    }

    public async Task<IEnumerable<FMFileInfo>> GetFilesAsync(string path)
    {
        if (_storageProviders.TryGetValue(path, out var provider))
        {
            return await provider.GetFilesAsync(path);
        }
        else
        {
            var localProvider = new LocalStorageProvider();
            return await localProvider.GetFilesAsync(path);
        }
    }

    public async Task<IEnumerable<FMFileInfo>> SearchAsync(string searchTerm)
    {
        return await _fileIndexer.SearchAsync(searchTerm);
    }

    public async Task AddMoleAsync(string path, MoleType type = MoleType.Local, string provider = "Default")
    {
        _options.Moles.Add(new Mole { Path = path, Type = type, Provider = provider });
        IStorageProvider storageProvider = CreateStorageProvider(type, provider);
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

    public async Task<IEnumerable<FMDirectoryInfo>> GetDirectoriesAsync(string path)
    {
        if (_storageProviders.TryGetValue(path, out var provider))
        {
            return await provider.GetDirectoriesAsync(path);
        }
        else
        {
            var localProvider = new LocalStorageProvider();
            return await localProvider.GetDirectoriesAsync(path);
        }
    }

    public async Task<long> GetTotalSizeAsync(string path)
    {
        long totalSize = 0;
        var files = await GetFilesAsync(path);
        foreach (var file in files)
        {
            totalSize += file.Size;
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

    public void StartInitialScan()
    {
        Task.Run(async () =>
        {
            await InitialScanAsync();
            IsInitialScanComplete = true;
            InitialScanCompleted?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task InitialScanAsync()
    {
        foreach (var mole in _options.Moles)
        {
            await ScanDirectoryAsync(mole.Path);
        }
    }

    private async Task ScanDirectoryAsync(string path)
    {
        try
        {
            var files = await _storageProviders[path].GetFilesAsync(path);
            foreach (var file in files)
            {
                await _fileIndexer.IndexFileAsync(file);
            }

            var directories = await _storageProviders[path].GetDirectoriesAsync(path);
            foreach (var directory in directories)
            {
                await ScanDirectoryAsync(directory.FullPath);
            }
        }
        catch (Exception ex)
        {
            // 로그 기록 또는 에러 처리
            Console.WriteLine($"Error scanning directory {path}: {ex.Message}");
        }
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
            _fileSystemWatcher.Dispose();
            (_fileIndexer as IDisposable)?.Dispose();
            foreach (var provider in _storageProviders.Values)
            {
                (provider as IDisposable)?.Dispose();
            }
            _debouncer.Dispose();
        }
    }
}