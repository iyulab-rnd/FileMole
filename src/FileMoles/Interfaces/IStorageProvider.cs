namespace FileMoles.Interfaces;

public interface IStorageProvider : IDisposable, IAsyncDisposable
{
    Task<FileInfo> GetFileAsync(string filePath);
    IAsyncEnumerable<FileInfo> GetFilesAsync(string path);
    IAsyncEnumerable<DirectoryInfo> GetDirectoriesAsync(string path);
    Task<Stream> OpenFileAsync(string path, FileMode mode);
    Task CreateDirectoryAsync(string path);
    Task DeleteFileAsync(string path);
    Task DeleteDirectoryAsync(string path, bool recursive);
    Task<bool> ExistsAsync(string path);
    Task MoveAsync(string sourcePath, string destinationPath);
    Task CopyAsync(string sourcePath, string destinationPath);
    Task RenameAsync(string fullPath, string newFileName);
    Task DeleteAsync(string fullPath);
}