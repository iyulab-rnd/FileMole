using FileMoles.Data;
using FileMoles.Internal;
using System.Runtime.CompilerServices;

namespace FileMoles.Indexing;

internal class FileIndexer : IDisposable, IAsyncDisposable
{
    private readonly IUnitOfWork _unitOfWork;
    private bool _disposed;

    public FileIndexer(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> IndexFileAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (file.Attributes.HasFlag(FileAttributes.Directory) || 
            IOHelper.IsHidden(file.FullName))
        {
            return false;
        }

        try
        {
            var fileIndex = FileIndex.CreateNew(file);

            await _unitOfWork.BeginTransactionAsync();
            await _unitOfWork.FileIndices.AddAsync(fileIndex);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error indexing file: {ex.Message}");
            return false;
        }
    }

    public async IAsyncEnumerable<FileInfo> SearchAsync(string searchTerm, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = await _unitOfWork.FileIndices.SearchAsync(searchTerm);
        foreach (var fileIndex in list)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _ = RefreshFileIndexAsync(fileIndex);

            var file = new FileInfo(fileIndex.FullPath);
            if (file.Exists && !file.Attributes.HasFlag(FileAttributes.Directory))
            {
                yield return file;
            }
        }
    }

    private async Task RefreshFileIndexAsync(FileIndex fileIndex)
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
                await IndexFileAsync(file);
            }
        }
        else
        {
            await _unitOfWork.FileIndices.DeleteAsync(fileIndex);
        }
    }

    public async Task<bool> HasFileChangedAsync(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (file.Exists == false)
        {
            return true;
        }

        var indexedFile = await _unitOfWork.FileIndices.GetByFullPathAsync(file.FullName);
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
        return _unitOfWork.FileIndices.GetByFullPathAsync(fullPath);
    }

    public async Task RemoveFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.FileIndices.DeleteByPathAsync(fullPath);
    }

    public async Task<int> GetFileCountAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.FileIndices.GetCountAsync(path);
    }

    public async Task ClearDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await _unitOfWork.FileIndices.ClearAsync();
    }

    internal async Task TryIndexFileAsync(FileInfo fileInfo)
    {
        if (!fileInfo.Attributes.HasFlag(FileAttributes.Directory) && await HasFileChangedAsync(fileInfo))
        {
            await IndexFileAsync(fileInfo);
        }
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
            _unitOfWork.Dispose();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            Dispose(false);
            await DisposeAsyncCore();
            GC.SuppressFinalize(this);
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await _unitOfWork.DisposeAsync();
    }
}
