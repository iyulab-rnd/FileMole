
namespace FileMoles.Storage
{
    internal class NotyetStorageProvider : IStorageProvider
    {
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

        public Task<IEnumerable<DirectoryInfo>> GetDirectoriesAsync(string path)
        {
            throw new NotImplementedException();
        }

        public Task<FileInfo> GetFileAsync(string filePath)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<FileInfo>> GetFilesAsync(string path)
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

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}