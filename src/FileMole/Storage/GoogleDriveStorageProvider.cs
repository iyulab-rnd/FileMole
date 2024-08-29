using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMole.Storage
{
    internal class GoogleDriveStorageProvider : IStorageProvider
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

        public Task<IEnumerable<FMDirectoryInfo>> GetDirectoriesAsync(string path)
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
}
