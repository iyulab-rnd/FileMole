using FileMoles.Data;
using FileMoles.Internals;

namespace FileMoles.Indexing;

public class FileIndexer : IDisposable, IAsyncDisposable
{
    private readonly DbContext _dbContext;
    private bool _disposed = false;

    public FileIndexer(string dbPath)
    {
        _dbContext = Resolver.ResolveDbContext(dbPath);
    }

    public async Task<bool> IndexFileAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (FileMoleUtils.IsHidden(file.FullName))
        {
            return false;
        }

        try
        {
            var fileIndex = new FileIndex(file.FullName)
            {
                Size = file.Length,
                Created = file.CreationTime,
                Modified = file.LastWriteTime,
                Attributes = file.Attributes
            };

            return await _dbContext.FileIndexies.AddOrUpdateAsync(fileIndex, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error indexing file: {ex.Message}");
            return false;
        }
    }

    public async Task<IEnumerable<FileInfo>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var fileIndexes = await _dbContext.FileIndexies.SearchAsync(searchTerm, cancellationToken);
        var results = new List<FileInfo>();

        foreach (var fileIndex in fileIndexes)
        {
            var file = new FileInfo(fileIndex.FullPath);
            if (file.Exists)
            {
                file.Refresh();

                if (file.CreationTime != fileIndex.Created ||
                    file.LastWriteTime != fileIndex.Modified ||
                    file.Attributes != fileIndex.Attributes ||
                    file.Length != fileIndex.Size)
                {
                    await IndexFileAsync(file, cancellationToken);
                }

                results.Add(file);
            }
            else
            {
                await _dbContext.FileIndexies.RemoveAsync(fileIndex.FullPath, cancellationToken);
            }
        }

        return results;
    }

    public async Task<bool> HasFileChangedAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (file.Exists == false)
        {
            return true;
        }

        var indexedFile = await _dbContext.FileIndexies.GetAsync(file.FullName, cancellationToken);
        if (indexedFile == null)
        {
            return true;
        }

        return indexedFile.Size != file.Length ||
            indexedFile.Created != file.CreationTime ||
            indexedFile.Modified != file.LastWriteTime ||
            indexedFile.Attributes != file.Attributes;
    }

    public Task<FileIndex?> GetIndexedFileInfoAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return _dbContext.FileIndexies.GetAsync(fullPath, cancellationToken);
    }

    public Task RemoveFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return _dbContext.FileIndexies.RemoveAsync(fullPath, cancellationToken);
    }

    public Task<int> GetFileCountAsync(string path, CancellationToken cancellationToken = default)
    {
        return _dbContext.FileIndexies.GetCountAsync(path, cancellationToken);
    }

    public Task ClearDatabaseAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.FileIndexies.ClearAsync(cancellationToken);
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
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
    }
}