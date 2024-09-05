using FileMoles.Storage;
using FileMoles.Events;
using FileMoles.Indexing;
using System.Text.Json;
using FileMoles.Utils;

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

    // 감시 경로의 모든 파일, 디렉토리에 대한 이벤트를 수신하고 전파합니다.
    public event EventHandler<FileMoleEventArgs>? FileCreated;
    public event EventHandler<FileMoleEventArgs>? FileChanged;
    public event EventHandler<FileMoleEventArgs>? FileDeleted;
    public event EventHandler<FileMoleEventArgs>? FileRenamed;

    public event EventHandler<FileMoleEventArgs>? DirectoryCreated;
    public event EventHandler<FileMoleEventArgs>? DirectoryChanged;
    public event EventHandler<FileMoleEventArgs>? DirectoryDeleted;
    public event EventHandler<FileMoleEventArgs>? DirectoryRenamed;

    // 추적 중인 파일의 내용이 변경될 때 발생합니다.
    public event EventHandler<FileContentChangedEventArgs>? FileContentChanged;

    public bool IsInitialScanComplete { get; private set; }
    public event EventHandler? InitialScanCompleted;

    internal FileMole(FileMoleOptions options)
    {
        _options = options;
        _fileIndexer = new FileIndexer(Functions.GetDatabasePath(options.GetDataPath()));
        _fileSystemWatcher = new FMFileSystemWatcher(options, _fileIndexer);
        _storageProviders = [];

        _moleTrackDebouncer = new Debouncer<FileSystemEvent>(_options.DebounceTime, OnMoleTrackDebouncedEvents);

        _moleConfigPath = Functions.GetTrackConfigPath(options.GetDataPath());

        InitializeStorageProviders();
        InitializeFileWatcher();
        LoadMoleTrackConfig();
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

    private void HandleDirectoryEvent(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        raiseEvent(e);
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

    private async Task HandleFileEventAsync(FileSystemEvent e, Action<FileSystemEvent> raiseEvent)
    {
        try
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
        catch (Exception)
        {
            return;
        }
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
                        if (diff != null && (diff.IsChanged || diff.IsInitial))
                        {
                            // 초기 백업 또는 변경사항이 있을 때 이벤트 발생
                            FileContentChanged?.Invoke(this, new FileContentChangedEventArgs(e, diff));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing file {e.FullPath}: {ex.Message}");
                    }
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

    public async Task<FileInfo> GetFileAsync(string filePath)
    {
        var provider = GetProviderForPath(filePath);
        if (provider != null)
        {
            return await provider.GetFileAsync(filePath);
        }
        else
        {
            throw new ArgumentException($"No storage provider found for filePath: {filePath}");
        }
    }

    public async Task<IEnumerable<FileInfo>> GetFilesAsync(string path)
    {
        var provider = GetProviderForPath(path);
        if (provider != null)
        {
            return await provider.GetFilesAsync(path);
        }
        else
        {
            throw new ArgumentException($"No storage provider found for path: {path}");
        }
    }

    public async Task<IEnumerable<FileInfo>> SearchFilesAsync(string searchTerm)
    {
        return await _fileIndexer.SearchAsync(searchTerm);
    }

    public async Task<IEnumerable<DirectoryInfo>> GetDirectoriesAsync(string path)
    {
        var provider = GetProviderForPath(path);
        if (provider != null)
        {
            return await provider.GetDirectoriesAsync(path);
        }
        else
        {
            throw new ArgumentException($"No storage provider found for path: {path}");
        }
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
        var files = await GetFilesAsync(path);
        foreach (var file in files)
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
                if (file.Length <= _options.MaxFileSizeBytes)
                {
                    await _fileIndexer.IndexFileAsync(file);
                }
            }

            var directories = await _storageProviders[path].GetDirectoriesAsync(path);
            foreach (var directory in directories)
            {
                await ScanDirectoryAsync(directory.FullName);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error scanning directory {path}: {ex.Message}");
        }
    }

    private IStorageProvider? GetProviderForPath(string path)
    {
        return _storageProviders
            .Where(kvp => path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Key.Length)
            .Select(kvp => kvp.Value)
            .FirstOrDefault();
    }

    public async Task<bool> EnableMoleTrackAsync(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            await Task.Run(() => EnableMoleTrackForPath(path));
            SaveMoleTrackConfig();
            return true;
        }
        return false;
    }

    public async Task<bool> DisableMoleTrackAsync(string path)
    {
        if (_moleTracks.ContainsKey(path))
        {
            await Task.Run(() => DisableMoleTrackForPath(path));
            SaveMoleTrackConfig();
            return true;
        }
        return false;
    }

    public bool AddIgnorePattern(string path, string pattern)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            moleTrack.AddIgnorePattern(pattern);
            return true;
        }
        return false;
    }

    public bool AddIncludePattern(string path, string pattern)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            moleTrack.AddIncludePattern(pattern);
            return true;
        }
        return false;
    }

    public List<string> GetIgnorePatterns(string path)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            return moleTrack.GetIgnorePatterns();
        }
        return [];
    }

    public List<string> GetIncludePatterns(string path)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            return moleTrack.GetIncludePatterns();
        }
        return [];
    }

    public bool IsMoleTrackEnabled(string path)
    {
        return _moleTracks.ContainsKey(path);
    }

    public List<string> GetAllTrackedPaths()
    {
        return new List<string>(_moleTracks.Keys);
    }

    public async Task<List<string>> GetTrackedFilesAsync(string path)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            return await moleTrack.GetTrackedFilesAsync();
        }
        return [];
    }

    private void EnableMoleTrackForPath(string path)
    {
        if (File.Exists(path))
        {
            EnableMoleTrackForFile(path);
        }
        else if (Directory.Exists(path))
        {
            EnableMoleTrackForDirectory(path);
        }
    }

    private void DisableMoleTrackForPath(string path)
    {
        if (_moleTracks.TryGetValue(path, out var moleTrack))
        {
            moleTrack.Dispose();
            _moleTracks.Remove(path);
        }
    }

    private void EnableMoleTrackForFile(string filePath)
    {
        var directoryPath = Path.GetDirectoryName(filePath)!;
        if (!_moleTracks.TryGetValue(directoryPath, out FileMoleTrack? value))
        {
            value = new FileMoleTrack(directoryPath);
            _moleTracks[directoryPath] = value;
        }
        value.AddTrackedFile(filePath);
    }

    private void EnableMoleTrackForDirectory(string directoryPath)
    {
        if (!_moleTracks.TryGetValue(directoryPath, out FileMoleTrack? _))
        {
            var value = new FileMoleTrack(directoryPath);
            _moleTracks[directoryPath] = value;
        }
    }

    private void SaveMoleTrackConfig()
    {
        var config = new FileMoleTrackConfig
        {
            TrackedPaths = new List<string>(_moleTracks.Keys)
        };

        FileSafe.WriteAllTextWithRetry(_moleConfigPath, JsonSerializer.Serialize(config));
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
                    EnableMoleTrackAsync(trackedPath).Forget();
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
                moleTrack?.Dispose();
            }
            _moleTracks.Clear();
        }
    }
}