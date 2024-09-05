using FileMoles.Data;
using FileMoles.Internals;

namespace FileMoles.Indexing;

public class FileIndexer : IDisposable, IAsyncDisposable
{
    private readonly DbContext _dbContext;
    private bool _disposed = false;

    public FileIndexer(string dbPath)
    {
        if (Directory.Exists(Path.GetDirectoryName(dbPath)) is false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        }

        _dbContext = new DbContext(dbPath);
    }

    public async Task<bool> IndexFileAsync(FileInfo file)
    {
        if (FileMoleUtils.IsHidden(file.FullName))
        {
            return false;
        }

        try
        {
            var fileIndex = new FileIndex(file.FullName)
            {
                Length = file.Length,
                CreationTime = file.CreationTime,
                LastWriteTime = file.LastWriteTime,
                LastAccessTime = file.LastAccessTime,
                Attributes = file.Attributes
            };

            return await _dbContext.FileIndexies.AddOrUpdateAsync(fileIndex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error indexing file: {ex.Message}");
            return false;
        }
    }

    public async Task<IEnumerable<FileInfo>> SearchAsync(string searchTerm)
    {
        var fileIndexes = await _dbContext.FileIndexies.SearchAsync(searchTerm);
        var results = new List<FileInfo>();

        foreach (var fileIndex in fileIndexes)
        {
            var file = new FileInfo(fileIndex.FullPath);
            if (file.Exists)
            {
                file.Refresh();

                if (file.CreationTime != fileIndex.CreationTime ||
                    file.LastWriteTime != fileIndex.LastWriteTime ||
                    file.LastAccessTime != fileIndex.LastAccessTime ||
                    file.Attributes != fileIndex.Attributes ||
                    file.Length != fileIndex.Length)
                {
                    await IndexFileAsync(file);
                }

                results.Add(file);
            }
            else
            {
                await _dbContext.FileIndexies.RemoveAsync(fileIndex.FullPath);
            }
        }

        return results;
    }

    public async Task<bool> HasFileChangedAsync(FileInfo file)
    {
        if (file.Exists == false)
        {
            return true;
        }

        var indexedFile = await _dbContext.FileIndexies.GetAsync(file.FullName);
        if (indexedFile == null)
        {
            return true;
        }

        return indexedFile.Length != file.Length ||
            indexedFile.CreationTime != file.CreationTime ||
            indexedFile.LastWriteTime != file.LastWriteTime ||
            indexedFile.LastAccessTime != file.LastAccessTime ||
            indexedFile.Attributes != file.Attributes;
    }

    public Task<FileIndex?> GetIndexedFileInfoAsync(string fullPath)
    {
        return _dbContext.FileIndexies.GetAsync(fullPath);
    }

    public Task RemoveFileAsync(string fullPath)
    {
        return _dbContext.FileIndexies.RemoveAsync(fullPath);
    }

    public Task<int> GetFileCountAsync(string path)
    {
        return _dbContext.FileIndexies.GetCountAsync(path);
    }

    public Task ClearDatabaseAsync()
    {
        return _dbContext.FileIndexies.ClearAsync();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _dbContext.Dispose();
            }

            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync().ConfigureAwait(false);
        }
    }
}