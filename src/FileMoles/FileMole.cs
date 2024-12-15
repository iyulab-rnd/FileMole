using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using FileMoles.Core.Interfaces;
using FileMoles.Core.Models;
using FileMoles.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace FileMoles;

public class FileMole : IDisposable
{
    private readonly ConcurrentDictionary<string, IStorageProvider> _providers;
    private readonly IFileSystemCache _cache;
    private readonly IFileSystemMonitor _monitor;
    private readonly ILogger<FileMole> _logger;
    private bool _disposed;

    public FileMole(
        IFileSystemCache cache,
        IFileSystemMonitor monitor,
        ILogger<FileMole> logger,
        IEnumerable<IStorageProvider> providers = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _providers = new ConcurrentDictionary<string, IStorageProvider>();
        if (providers != null)
        {
            foreach (var provider in providers)
            {
                RegisterProvider(provider);
            }
        }
    }

    public void RegisterProvider(IStorageProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrEmpty(provider.ProviderId))
            throw new ArgumentException("Provider ID cannot be null or empty", nameof(provider));

        if (!_providers.TryAdd(provider.ProviderId, provider))
        {
            throw new InvalidOperationException($"Provider with ID '{provider.ProviderId}' is already registered.");
        }

        _logger.LogInformation("Registered storage provider: {ProviderId}", provider.ProviderId);
    }

    public async Task<FileSystemItem> GetItemAsync(string providerId, string path)
    {
        ValidateProviderAndPath(providerId, path);

        try
        {
            // Try cache first
            var cachedItem = await _cache.GetCachedItemAsync(providerId, path);
            if (cachedItem != null)
            {
                _logger.LogDebug("Cache hit for {ProviderId}:{Path}", providerId, path);
                return cachedItem;
            }

            // Get from provider
            var provider = _providers[providerId];
            var item = await provider.GetItemAsync(path);

            // Cache the result
            await _cache.CacheItemAsync(providerId, path, item);

            return item;
        }
        catch (Exception ex) when (ex is not FileMoleException)
        {
            _logger.LogError(ex, "Error getting item {ProviderId}:{Path}", providerId, path);
            throw new StorageOperationException("GetItem", ex.Message);
        }
    }

    public async Task<Stream> OpenFileAsync(string providerId, string path, FileAccess access)
    {
        ValidateProviderAndPath(providerId, path);

        try
        {
            var provider = _providers[providerId];
            return access switch
            {
                FileAccess.Read => await provider.OpenReadAsync(path),
                FileAccess.Write => await provider.OpenWriteAsync(path),
                FileAccess.ReadWrite => throw new NotSupportedException("ReadWrite access is not supported"),
                _ => throw new ArgumentException($"Unsupported file access mode: {access}")
            };
        }
        catch (Exception ex) when (ex is not FileMoleException)
        {
            _logger.LogError(ex, "Error opening file {ProviderId}:{Path} with access {Access}",
                providerId, path, access);
            throw new StorageOperationException("OpenFile", ex.Message);
        }
    }

    public async Task<IEnumerable<FileSystemItem>> ListItemsAsync(string providerId, string path = "/")
    {
        ValidateProviderAndPath(providerId, path);

        try
        {
            var provider = _providers[providerId];
            var items = await provider.ListItemsAsync(path);

            // Cache all items
            await _cache.CacheItemsAsync(providerId, path, items);

            return items;
        }
        catch (Exception ex) when (ex is not FileMoleException)
        {
            _logger.LogError(ex, "Error listing items for {ProviderId}:{Path}", providerId, path);
            throw new StorageOperationException("ListItems", ex.Message);
        }
    }

    public async Task DeleteAsync(string providerId, string path, bool recursive = false)
    {
        ValidateProviderAndPath(providerId, path);

        try
        {
            var provider = _providers[providerId];
            await provider.DeleteAsync(path, recursive);
            await _cache.InvalidateCacheAsync(providerId, path);
        }
        catch (Exception ex) when (ex is not FileMoleException)
        {
            _logger.LogError(ex, "Error deleting {ProviderId}:{Path}", providerId, path);
            throw new StorageOperationException("Delete", ex.Message);
        }
    }

    public async Task MoveAsync(string providerId, string sourcePath, string destinationPath)
    {
        ValidateProviderAndPath(providerId, sourcePath);
        ValidateProviderAndPath(providerId, destinationPath);

        try
        {
            var provider = _providers[providerId];
            await provider.MoveAsync(sourcePath, destinationPath);

            await Task.WhenAll(
                _cache.InvalidateCacheAsync(providerId, sourcePath),
                _cache.InvalidateCacheAsync(providerId, destinationPath)
            );
        }
        catch (Exception ex) when (ex is not FileMoleException)
        {
            _logger.LogError(ex, "Error moving {ProviderId}:{SourcePath} to {DestinationPath}",
                providerId, sourcePath, destinationPath);
            throw new StorageOperationException("Move", ex.Message);
        }
    }

    public async Task CopyAsync(string providerId, string sourcePath, string destinationPath)
    {
        ValidateProviderAndPath(providerId, sourcePath);
        ValidateProviderAndPath(providerId, destinationPath);

        try
        {
            var provider = _providers[providerId];
            await provider.CopyAsync(sourcePath, destinationPath);
            await _cache.InvalidateCacheAsync(providerId, destinationPath);
        }
        catch (Exception ex) when (ex is not FileMoleException)
        {
            _logger.LogError(ex, "Error copying {ProviderId}:{SourcePath} to {DestinationPath}",
                providerId, sourcePath, destinationPath);
            throw new StorageOperationException("Copy", ex.Message);
        }
    }

    private void ValidateProviderAndPath(string providerId, string path)
    {
        if (string.IsNullOrEmpty(providerId))
            throw new ArgumentException("Provider ID cannot be null or empty", nameof(providerId));

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        if (!_providers.ContainsKey(providerId))
            throw new ProviderNotFoundException(providerId);
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
            foreach (var provider in _providers.Values)
            {
                (provider as IDisposable)?.Dispose();
            }
            (_monitor as IDisposable)?.Dispose();
            (_cache as IDisposable)?.Dispose();
        }

        _disposed = true;
    }
}