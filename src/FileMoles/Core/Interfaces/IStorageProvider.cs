using FileMoles.Core.Models;

namespace FileMoles.Core.Interfaces;

public interface IStorageProvider
{
    string ProviderId { get; }
    Task<IEnumerable<FileSystemItem>> ListItemsAsync(string path);
    Task<FileSystemItem> GetItemAsync(string path);
    Task<Stream> OpenReadAsync(string path);
    Task<Stream> OpenWriteAsync(string path);
    Task DeleteAsync(string path, bool recursive = false);
    Task MoveAsync(string sourcePath, string destinationPath);
    Task CopyAsync(string sourcePath, string destinationPath);
    Task RenameAsync(string path, string newName);
    Task<bool> ExistsAsync(string path);
    Task<FileSystemItem> CreateDirectoryAsync(string path);
    Task<long> GetAvailableSpaceAsync(string path);
}
