
namespace FileMole.Storage;

internal class CloudStorageProvider : IStorageProvider
{
    private readonly string provider;

    public CloudStorageProvider(string provider)
    {
        this.provider = provider;
    }

    public Task CopyAsync(string sourcePath, string destinationPath)
    {
        throw new NotImplementedException();
    }

    public Task CreateDirectoryAsync(string path)
    {
        throw new NotImplementedException();
    }

    public Task DeleteDirectoryAsync(string path, bool recursive)
    {
        throw new NotImplementedException();
    }

    public Task DeleteFileAsync(string path)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsAsync(string path)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<FMDirectoryInfo>> GetDirectoriesAsync(string path)
    {
        throw new NotImplementedException();
    }

    public Task<FMFileInfo> GetFileAsync(string filePath)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<FMFileInfo>> GetFilesAsync(string path)
    {
        throw new NotImplementedException();
    }

    public Task MoveAsync(string sourcePath, string destinationPath)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> OpenFileAsync(string path, FileMode mode)
    {
        throw new NotImplementedException();
    }
}
