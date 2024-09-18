using FileMoles.Data;
using FileMoles.Internal;
using System.Runtime.CompilerServices;

namespace FileMoles.Indexing;

internal class FileIndexer(DbContext dbContext) : IDisposable
{
    private readonly DbContext _dbContext = dbContext;
    private bool _disposed;

    public async Task<bool> IndexFileAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (file.Attributes.HasFlag(FileAttributes.Directory) ||
            IOHelper.IsHidden(file.FullName))
        {
            return false;
        }

        var fileIndex = FileIndex.CreateNew(file);
        await _dbContext.FileIndices.UpsertAsync(fileIndex, cancellationToken);
        return true;
    }

    public async IAsyncEnumerable<FileInfo> SearchAsync(string search, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.FileIndices.SearchAsync(search, cancellationToken);

        foreach (var fileIndex in list)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 비동기적으로 파일 인덱스를 갱신합니다.
            RefreshFileIndexAsync(fileIndex, cancellationToken).Forget();

            var fullPath = Path.Combine(fileIndex.Directory, fileIndex.Name);
            var file = new FileInfo(fullPath);
            if (file.Exists && !file.Attributes.HasFlag(FileAttributes.Directory))
            {
                yield return file;
            }
        }
    }

    private async Task RefreshFileIndexAsync(FileIndex fileIndex, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(fileIndex.Directory, fileIndex.Name);
        var file = new FileInfo(fullPath);
        if (file.Exists)
        {
            file.Refresh();

            if (fileIndex.IsChanged(file))
            {
                await IndexFileAsync(file, cancellationToken);
            }
        }
        else
        {
            await _dbContext.FileIndices.DeleteAsync(fileIndex, cancellationToken);
        }
    }

    public async Task<bool> HasFileChangedAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (!file.Exists)
        {
            return true;
        }

        var directory = file.DirectoryName ?? string.Empty;
        var name = file.Name;

        var indexedFile = await _dbContext.FileIndices.GetByDirectoryAndNameAsync(directory, name, cancellationToken);
        if (indexedFile == null)
        {
            return true;
        }

        return indexedFile.IsChanged(file);
    }

    public async Task<List<FileIndex>> GetFileIndicesByDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        return await _dbContext.FileIndices.GetByDirectoryAsync(directory, cancellationToken);
    }

    public async Task UpsertFileIndicesAsync(List<FileIndex> fileIndices, CancellationToken cancellationToken)
    {
        await _dbContext.FileIndices.UpsertAsync(fileIndices, cancellationToken);
    }

    public async Task DeleteFileIndicesAsync(List<FileIndex> fileIndices, CancellationToken cancellationToken)
    {
        foreach (var fileIndex in fileIndices)
        {
            await _dbContext.FileIndices.DeleteAsync(fileIndex, cancellationToken);
        }
    }

    public async Task RemoveFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileName(filePath);

        await _dbContext.FileIndices.DeleteByDirectoryAndNameAsync(directory, name, cancellationToken);
    }
    
    public async Task RemoveDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        await _dbContext.FileIndices.DeleteByDirectoryAsync(directoryPath, cancellationToken);
    }

    public async Task<int> GetFileCountAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FileIndices.GetCountAsync(path, cancellationToken);
    }
    
    internal async Task<long> GetTotalSizeAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FileIndices.GetTotalSizeAsync(path, cancellationToken);
    }

    public async Task RemoveEntriesNotScannedAfterAsync(DateTime scanStartTime, CancellationToken cancellationToken = default)
    {
        await _dbContext.FileIndices.DeleteEntriesNotScannedAfterAsync(scanStartTime, cancellationToken);
    }

    internal async Task TryIndexFileAsync(FileInfo fileInfo, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (fileInfo.Attributes.HasFlag(FileAttributes.Directory)) return;

        if (await HasFileChangedAsync(fileInfo, cancellationToken))
        {
            await IndexFileAsync(fileInfo, cancellationToken);
        }
    }

    internal async Task UpdateLastScannedAsync(IEnumerable<FileIndex> fileIndices, CancellationToken cancellationToken = default)
    {
        await _dbContext.FileIndices.UpdateLastScannedAsync(fileIndices, cancellationToken);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _dbContext.Dispose();
        }

        _disposed = true;
    }
}