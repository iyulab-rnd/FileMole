using FileMoles.Storage;
using FileMoles.Events;
using FileMoles.Indexing;
using System.Text.Json;
using System.Collections.Concurrent;
using LibGit2Sharp;

namespace FileMoles;

public class FileMole : IDisposable
{
    private readonly FileMoleOptions _options;
    private readonly FileIndexer _fileIndexer;
    private readonly FMFileSystemWatcher _fileSystemWatcher;
    private readonly Dictionary<string, IStorageProvider> _storageProviders;
    private readonly Dictionary<string, FileMoleTrack> _moleTracks = [];
    private readonly Debouncer<FileSystemEvent> _moleTrackDebouncer;
    private readonly string _moleConfigPath;

    public event EventHandler<FileMoleEventArgs>? FileCreated;
    public event EventHandler<FileMoleEventArgs>? FileChanged;
    public event EventHandler<FileMoleEventArgs>? FileDeleted;
    public event EventHandler<FileMoleEventArgs>? FileRenamed;
    public event EventHandler<FileMoleTrackChangedEventArgs>? MoleTrackChanged;

    public bool IsInitialScanComplete { get; private set; }
    public event EventHandler? InitialScanCompleted;

    public FileMole(FileMoleOptions options)
    {
        _options = options;
        _fileIndexer = new FileIndexer(options);
        _fileSystemWatcher = new FMFileSystemWatcher(_fileIndexer);
        _storageProviders = new Dictionary<string, IStorageProvider>();

        _moleTrackDebouncer = new Debouncer<FileSystemEvent>(_options.DebounceTime, OnMoleTrackDebouncedEvents);

        _moleConfigPath = Functions.GetTrackConfigPath();

        InitializeStorageProviders();
        InitializeFileWatcher();
        LoadMoleTrackConfig();
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
        _fileSystemWatcher.FileCreated += async (sender, e) => await HandleFileEventAsync(e, RaiseFileCreatedEvent);
        _fileSystemWatcher.FileChanged += async (sender, e) => await HandleFileEventAsync(e, RaiseFileChangedEvent);
        _fileSystemWatcher.FileDeleted += async (sender, e) => await HandleFileEventAsync(e, RaiseFileDeletedEvent);
        _fileSystemWatcher.FileRenamed += async (sender, e) => await HandleFileEventAsync(e, RaiseFileRenamedEvent);

        foreach (var mole in _options.Moles)
        {
            _fileSystemWatcher.WatchDirectoryAsync(mole.Path);
        }
    }

    private async Task HandleFileEventAsync(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
        var directoryPath = Path.GetDirectoryName(e.FullPath)!;
        if (_moleTracks.TryGetValue(directoryPath, out var moleTrack))
        {
            if (e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Changed)
            {
                if (moleTrack.ShouldTrackFile(e.FullPath))
                {
                    await _moleTrackDebouncer.DebounceAsync(e.FullPath, e);
                }
            }
            else if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                await moleTrack.RemoveFileAsync(e.FullPath);
            }
            else if (e.ChangeType == WatcherChangeTypes.Renamed)
            {
                await moleTrack.RemoveFileAsync(e.OldFullPath!);
                if (moleTrack.ShouldTrackFile(e.FullPath))
                {
                    await _moleTrackDebouncer.DebounceAsync(e.FullPath, e);
                }
            }
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

    private async Task OnMoleTrackDebouncedEvents(IEnumerable<FileSystemEvent> events)
    {
        foreach (var e in events)
        {
            var directoryPath = Path.GetDirectoryName(e.FullPath)!;
            if (_moleTracks.TryGetValue(directoryPath, out var moleTrack))
            {
                if (moleTrack.ShouldTrackFile(e.FullPath))
                {
                    try
                    {
                        var diff = await moleTrack.TrackAndGetDiffAsync(e.FullPath);
                        if (diff == null) continue;

                        MoleTrackChanged?.Invoke(this, new FileMoleTrackChangedEventArgs(e, diff));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing file {e.FullPath}: {ex.Message}");
                    }
                }
            }
        }
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
            return await provider.GetFilesAsync(path);
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
                if (file.Size <= _options.MaxFileSizeBytes)
                {
                    await _fileIndexer.IndexFileAsync(file);
                }
            }

            var directories = await _storageProviders[path].GetDirectoriesAsync(path);
            foreach (var directory in directories)
            {
                await ScanDirectoryAsync(directory.FullPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning directory {path}: {ex.Message}");
        }
    }

    private void LoadMoleTrackConfig()
    {
        if (File.Exists(_moleConfigPath))
        {
            var config = JsonSerializer.Deserialize<FileMoleTrackConfig>(File.ReadAllText(_moleConfigPath));
            if (config == null) return;

            var validPaths = new List<string>();

            foreach (var trackedPath in config.TrackedPaths)
            {
                if (File.Exists(trackedPath) || Directory.Exists(trackedPath))
                {
                    EnableMoleTrack(trackedPath);
                    validPaths.Add(trackedPath);
                }
                else
                {
                    Console.WriteLine($"Warning: Tracked path no longer exists and will be removed from config: {trackedPath}");
                }
            }

            // Update the config if any invalid paths were found
            if (validPaths.Count != config.TrackedPaths.Count)
            {
                config.TrackedPaths = validPaths;
                SaveMoleTrackConfig();
            }
        }
    }

    private void SaveMoleTrackConfig()
    {
        var config = new FileMoleTrackConfig
        {
            TrackedPaths = _moleTracks.Keys.ToList()
        };
        File.WriteAllText(_moleConfigPath, JsonSerializer.Serialize(config));
    }

    public void EnableMoleTrack(string path)
    {
        if (File.Exists(path))
        {
            EnableMoleTrackForFile(path);
        }
        else if (Directory.Exists(path))
        {
            EnableMoleTrackForDirectory(path);
        }
        else
        {
            throw new ArgumentException($"Path does not exist: {path}");
        }

        SaveMoleTrackConfig();
    }

    private void EnableMoleTrackForFile(string filePath)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (!_moleTracks.ContainsKey(directoryPath))
        {
            _moleTracks[directoryPath] = new FileMoleTrack(directoryPath, _options);
        }
        _moleTracks[directoryPath].AddTrackedFile(filePath);
    }

    private void EnableMoleTrackForDirectory(string directoryPath)
    {
        if (!_moleTracks.ContainsKey(directoryPath))
        {
            _moleTracks[directoryPath] = new FileMoleTrack(directoryPath, _options);
        }

        // Add default ignore patterns
        _moleTracks[directoryPath].AddIgnorePattern(".mole");
        _moleTracks[directoryPath].AddIgnorePattern(".git");
        _moleTracks[directoryPath].AddIgnorePattern("*.tmp");
        _moleTracks[directoryPath].AddIgnorePattern("*.log");
    }

    public void AddIgnorePattern(string path, string pattern)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            moleTrack.AddIgnorePattern(pattern);
        }
        else
        {
            throw new ArgumentException($"No MoleTrack found for path: {path}");
        }
    }

    public void DisableMoleTrack(string path)
    {
        if (File.Exists(path))
        {
            DisableMoleTrackForFile(path);
        }
        else if (Directory.Exists(path))
        {
            DisableMoleTrackForDirectory(path);
        }

        SaveMoleTrackConfig();
    }

    private void DisableMoleTrackForFile(string filePath)
    {
        var directoryPath = Path.GetDirectoryName(filePath);
        if (_moleTracks.TryGetValue(directoryPath, out var moleTrack))
        {
            moleTrack.RemoveTrackedFile(filePath);
            if (!moleTrack.HasTrackedFiles)
            {
                moleTrack.Dispose();
                _moleTracks.Remove(directoryPath);
            }
        }
    }

    private void DisableMoleTrackForDirectory(string directoryPath)
    {
        if (_moleTracks.TryGetValue(directoryPath, out var moleTrack))
        {
            moleTrack.Dispose();
            _moleTracks.Remove(directoryPath);
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
            _moleTrackDebouncer.Dispose();
            foreach (var moleTrack in _moleTracks.Values)
            {
                moleTrack.Dispose();
            }
            _moleTracks.Clear();
        }
    }
}