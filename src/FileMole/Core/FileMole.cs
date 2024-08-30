using FileMole.Storage;
using FileMole.Events;
using FileMole.Indexing;
using System.Runtime.Intrinsics.X86;

namespace FileMole.Core;

public class FileMole : IDisposable
{
    private readonly FileMoleOptions _options;
    private readonly FileIndexer _fileIndexer;
    private readonly FMFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;

    public event EventHandler<FileMoleEventArgs>? FileCreated;
    public event EventHandler<FileMoleEventArgs>? FileChanged;
    public event EventHandler<FileMoleEventArgs>? FileDeleted;
    public event EventHandler<FileMoleEventArgs>? FileRenamed;

    public FileMole(FileMoleOptions options)
    {
        _options = options;
        _fileIndexer = new FileIndexer(options);
        _fileSystemWatcher = new FMFileSystemWatcher(TimeSpan.FromMilliseconds(300), _fileIndexer);
        _storageProviders = [];

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
            (MoleType.Cloud, "onedrive") => new CloudStorageProvider("OneDrive"),
            (MoleType.Cloud, _) => throw new NotSupportedException($"Unsupported cloud provider: {provider}"),
            _ => throw new NotSupportedException($"Unsupported storage type: {type}")
        };
    }

    private void InitializeFileWatcher()
    {
        _fileSystemWatcher.FileCreated += (sender, e) => RaiseFileCreatedEvent(e);
        _fileSystemWatcher.FileChanged += (sender, e) => RaiseFileChangedEvent(e);
        _fileSystemWatcher.FileDeleted += (sender, e) => RaiseFileDeletedEvent(e);
        _fileSystemWatcher.FileRenamed += (sender, e) => RaiseFileRenamedEvent(e);

        foreach (var mole in _options.Moles)
        {
            _fileSystemWatcher.WatchDirectoryAsync(mole.Path);
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
            // 기본적으로 로컬 스토리지 프로바이더 사용
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
        }
    }
}