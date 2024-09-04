using System.IO;

namespace FileMoles.Storage;

public interface IStorageProvider
{
    Task<FileInfo> GetFileAsync(string filePath);
    Task<IEnumerable<FileInfo>> GetFilesAsync(string path);
    Task<IEnumerable<DirectoryInfo>> GetDirectoriesAsync(string path);
    Task<Stream> OpenFileAsync(string path, FileMode mode);
    Task CreateDirectoryAsync(string path);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path, bool recursive);
    Task<bool> ExistsAsync(string path);
    Task MoveAsync(string sourcePath, string destinationPath);
    Task CopyAsync(string sourcePath, string destinationPath);
}