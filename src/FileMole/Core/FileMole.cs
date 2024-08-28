using FileMole.Storage;
using FileMole.Events;
using FileMole.Indexing;

namespace FileMole.Core;

public class FileMole : IFileMole
{
    private readonly FileMoleOptions _options;
    private readonly List<string> _watchedPaths = new List<string>();

    public event EventHandler<FileSystemEvent> FileSystemChanged;

    public FileMole(FileMoleOptions options)
    {
        _options = options;
        _options.FileSystemWatcher.FileSystemChanged += (sender, e) => FileSystemChanged?.Invoke(this, e);
    }

    public async Task WatchDirectoryAsync(string path)
    {
        if (!_watchedPaths.Contains(path))
        {
            await _options.FileSystemWatcher.WatchDirectoryAsync(path);
            _watchedPaths.Add(path);
        }
    }

    public async Task IndexAllAsync()
    {
        foreach (var path in _watchedPaths)
        {
            var files = await _options.StorageProvider.GetFilesAsync(path);
            foreach (var file in files)
            {
                await _options.FileIndexer.IndexFileAsync(file);
            }
        }
    }

    public async Task<IEnumerable<FMFileInfo>> SearchAsync(string searchTerm)
    {
        return await _options.FileIndexer.SearchAsync(searchTerm);
    }

    public void Dispose()
    {
        _options.FileSystemWatcher.Dispose();
    }
}