using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Runtime.Caching;
using Microsoft.EntityFrameworkCore;
using FileMoles.Core.Interfaces;
using FileMoles.Core.Models;
using FileMoles.Core.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Specialized;

namespace FileMoles.Cache;

public class FileSystemCache : IFileSystemCache, IDisposable
{
    private readonly CacheDbContext _dbContext;
    private readonly CacheOptions _options;
    private readonly ILogger<FileSystemCache> _logger;
    private readonly MemoryCache _memoryCache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks;
    private readonly Timer _maintenanceTimer;
    private bool _disposed;

    private const string MemoryCachePrefix = "FSCache_";
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(5);

    public FileSystemCache(
        CacheDbContext dbContext,
        IOptions<CacheOptions> options,
        ILogger<FileSystemCache> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _memoryCache = new MemoryCache("FileSystemCache", new NameValueCollection
        {
            { "CacheMemoryLimitMegabytes", "100" },
            { "PhysicalMemoryLimitPercentage", "10" },
            { "PollingInterval", "00:00:30" }
        });

        _locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _maintenanceTimer = new Timer(
            DoMaintenance,
            null,
            MaintenanceInterval,
            MaintenanceInterval);

        InitializeDatabase().GetAwaiter().GetResult();
    }

    private async Task InitializeDatabase()
    {
        try
        {
            await _dbContext.Database.MigrateAsync();

            // 인덱스 최적화
            if (!await _dbContext.Database.GetPendingMigrationsAsync().AnyAsync())
            {
                await _dbContext.Database.ExecuteSqlRawAsync(@"
                    CREATE INDEX IF NOT EXISTS IX_CachedItems_ExpirationTime ON CachedItems(ExpirationTime);
                    CREATE INDEX IF NOT EXISTS IX_CachedItems_LastAccess ON CachedItems(LastAccessTime);
                    VACUUM;");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize cache database");
            throw new CacheInitializationException("Failed to initialize cache database", ex);
        }
    }

    public async Task<FileSystemItem> GetCachedItemAsync(string providerId, string path)
    {
        ThrowIfDisposed();
        var cacheKey = GetCacheKey(providerId, path);

        // 먼저 메모리 캐시 확인
        if (_memoryCache.Get(cacheKey) is FileSystemItem memoryCachedItem)
        {
            _logger.LogDebug("Memory cache hit for {ProviderId}:{Path}", providerId, path);
            return memoryCachedItem;
        }

        var cacheLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        try
        {
            await cacheLock.WaitAsync();

            var cachedItem = await _dbContext.CachedItems
                .Include(i => i.Metadata)
                .AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.ProviderId == providerId &&
                    i.FullPath == path &&
                    i.ExpirationTime > DateTime.UtcNow);

            if (cachedItem == null)
            {
                _logger.LogDebug("Cache miss for {ProviderId}:{Path}", providerId, path);
                return null;
            }

            var item = ConvertToFileSystemItem(cachedItem);

            // 메모리 캐시에 추가
            var memoryCachePolicy = new CacheItemPolicy
            {
                AbsoluteExpiration = cachedItem.ExpirationTime,
                Priority = CacheItemPriority.Default
            };
            _memoryCache.Add(cacheKey, item, memoryCachePolicy);

            // 접근 시간 업데이트 (비동기로 처리)
            _ = UpdateLastAccessTimeAsync(cachedItem.Id);

            return item;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task CacheItemAsync(string providerId, string path, FileSystemItem item)
    {
        ThrowIfDisposed();
        var cacheKey = GetCacheKey(providerId, path);
        var cacheLock = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        try
        {
            await cacheLock.WaitAsync();

            // 캐시 크기 체크 및 정리
            await EnsureCacheSizeAsync();

            var cachedItem = new CachedItem
            {
                ProviderId = providerId,
                FullPath = path,
                Name = item.Name,
                IsDirectory = item.IsDirectory,
                Size = item.Size,
                CreationTime = item.CreationTime,
                LastAccessTime = DateTime.UtcNow,
                LastWriteTime = item.LastWriteTime,
                Extension = item.Extension,
                CacheTime = DateTime.UtcNow,
                ExpirationTime = DateTime.UtcNow.Add(_options.Expiration),
                Metadata = item.Metadata?
                    .Select(kvp => new ItemMetadata { Key = kvp.Key, Value = kvp.Value?.ToString() })
                    .ToList()
            };

            var existingItem = await _dbContext.CachedItems
                .Include(i => i.Metadata)
                .FirstOrDefaultAsync(i => i.ProviderId == providerId && i.FullPath == path);

            if (existingItem != null)
            {
                _dbContext.Entry(existingItem).CurrentValues.SetValues(cachedItem);
                existingItem.ExpirationTime = cachedItem.ExpirationTime;
                existingItem.LastAccessTime = DateTime.UtcNow;

                // 메타데이터 업데이트
                existingItem.Metadata.Clear();
                if (cachedItem.Metadata != null)
                {
                    foreach (var metadata in cachedItem.Metadata)
                    {
                        existingItem.Metadata.Add(metadata);
                    }
                }
            }
            else
            {
                await _dbContext.CachedItems.AddAsync(cachedItem);
            }

            await _dbContext.SaveChangesAsync();

            // 메모리 캐시 업데이트
            var memoryCachePolicy = new CacheItemPolicy
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.Add(_options.Expiration),
                Priority = CacheItemPriority.Default
            };
            _memoryCache.Set(cacheKey, item, memoryCachePolicy);

            _logger.LogDebug("Cached item {ProviderId}:{Path}", providerId, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching item {ProviderId}:{Path}", providerId, path);
            throw new CacheOperationException("Failed to cache item", ex);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task InvalidateCacheAsync(string providerId, string path)
    {
        ThrowIfDisposed();
        var cacheKey = GetCacheKey(providerId, path);

        // 메모리 캐시에서 제거
        _memoryCache.Remove(cacheKey);

        try
        {
            // 경로 패턴 매칭을 사용하여 하위 항목도 함께 제거
            var itemsToRemove = await _dbContext.CachedItems
                .Where(i => i.ProviderId == providerId &&
                           (i.FullPath == path || i.FullPath.StartsWith(path + "/")))
                .ToListAsync();

            if (itemsToRemove.Any())
            {
                _dbContext.CachedItems.RemoveRange(itemsToRemove);
                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("Invalidated {Count} cached items for {ProviderId}:{Path}",
                    itemsToRemove.Count, providerId, path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache for {ProviderId}:{Path}", providerId, path);
            throw new CacheOperationException("Failed to invalidate cache", ex);
        }
    }

    private async Task EnsureCacheSizeAsync()
    {
        try
        {
            var totalSize = await _dbContext.CachedItems.SumAsync(i => i.Size);
            if (totalSize > _options.MaxCacheSize)
            {
                var sizeToFree = (totalSize - _options.MaxCacheSize) + (_options.MaxCacheSize / 10); // 10% 여유 공간 확보

                // LRU 방식으로 캐시 정리
                var itemsToRemove = await _dbContext.CachedItems
                    .OrderBy(i => i.LastAccessTime)
                    .Select(i => new { i.Id, i.Size })
                    .ToListAsync();

                long freedSize = 0;
                var idsToRemove = new List<long>();

                foreach (var item in itemsToRemove)
                {
                    idsToRemove.Add(item.Id);
                    freedSize += item.Size;
                    if (freedSize >= sizeToFree)
                        break;
                }

                if (idsToRemove.Any())
                {
                    // 배치 처리로 삭제
                    await _dbContext.CachedItems
                        .Where(i => idsToRemove.Contains(i.Id))
                        .ExecuteDeleteAsync();

                    _logger.LogInformation(
                        "Cleaned up cache: removed {Count} items, freed {Size} bytes",
                        idsToRemove.Count, freedSize);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring cache size");
            throw new CacheOperationException("Failed to ensure cache size", ex);
        }
    }

    private async Task DoMaintenance(object state)
    {
        try
        {
            // 만료된 항목 제거
            var expiredItems = await _dbContext.CachedItems
                .Where(i => i.ExpirationTime < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            // 데이터베이스 최적화
            if (expiredItems > 0)
            {
                await _dbContext.Database.ExecuteSqlRawAsync("VACUUM;");
                _logger.LogInformation("Cache maintenance: removed {Count} expired items", expiredItems);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cache maintenance");
        }
    }

    private async Task UpdateLastAccessTimeAsync(long itemId)
    {
        try
        {
            await _dbContext.CachedItems
                .Where(i => i.Id == itemId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(b => b.LastAccessTime, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating last access time for item {ItemId}", itemId);
        }
    }

    private static string GetCacheKey(string providerId, string path)
        => $"{MemoryCachePrefix}{providerId}:{path}";

    private static FileSystemItem ConvertToFileSystemItem(CachedItem cachedItem)
    {
        return new FileSystemItem
        {
            Name = cachedItem.Name,
            FullPath = cachedItem.FullPath,
            IsDirectory = cachedItem.IsDirectory,
            Size = cachedItem.Size,
            CreationTime = cachedItem.CreationTime,
            LastAccessTime = cachedItem.LastAccessTime,
            LastWriteTime = cachedItem.LastWriteTime,
            Extension = cachedItem.Extension,
            Metadata = cachedItem.Metadata?
                .ToDictionary(m => m.Key, m => (object)m.Value)
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _maintenanceTimer?.Dispose();
            _memoryCache?.Dispose();

            foreach (var lockObj in _locks.Values)
            {
                lockObj.Dispose();
            }
            _locks.Clear();
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileSystemCache));
    }
}

public class CacheInitializationException : Exception
{
    public CacheInitializationException(string message, Exception inner = null)
        : base(message, inner) { }
}

public class CacheOperationException : Exception
{
    public CacheOperationException(string message, Exception inner = null)
        : base(message, inner) { }
}