using FileMoles.Cache;
using FileMoles.Core.Models;

namespace FileMoles.Core.Interfaces;

public interface IFileSystemCache
{
    Task<FileSystemItem> GetCachedItemAsync(string providerId, string path);
    Task CacheItemAsync(string providerId, string path, FileSystemItem item);
    Task InvalidateCacheAsync(string providerId, string path);
    Task<IEnumerable<FileSystemItem>> SearchCacheAsync(string providerId, string searchPattern, SearchOptions options);

    // 추가할 메서드들
    Task CacheItemsAsync(string providerId, string path, IEnumerable<FileSystemItem> items);
    Task<IEnumerable<FileSystemItem>> GetCachedItemsAsync(string providerId, string path);
    Task<bool> IsCachedAsync(string providerId, string path);
    Task ClearCacheAsync(string providerId);
    Task<long> GetCacheSizeAsync();
}
