using FileMole.Storage;
using FileMole.Events;
using FileMole.Indexing;

namespace FileMole.Core;

public class FileMole
{
    private readonly FileMoleOptions _options;
    private readonly FileIndexer _fileIndexer;
    private readonly FMFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;

    public event EventHandler<FileSystemEvent>? FileSystemChanged;

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
        _fileSystemWatcher.FileSystemChanged += async (sender, e) =>
        {
            FileSystemChanged?.Invoke(this, e);
            var fileInfo = new FileInfo(e.FullPath);
            var fmFileInfo = FMFileInfo.FromFileInfo(fileInfo);
            await _fileIndexer.IndexFileAsync(fmFileInfo);
        };

        foreach (var mole in _options.Moles)
        {
            _fileSystemWatcher.WatchDirectoryAsync(mole.Path);
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

    public void Dispose()
    {
        _fileSystemWatcher.Dispose();
        (_fileIndexer as IDisposable)?.Dispose();
    }
}
