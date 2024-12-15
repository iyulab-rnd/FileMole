using FileMoles.Core.Interfaces;
using FileMoles.Core.Models;
using FileMoles.Core.Exceptions;
using FileMoles.Common.Utils;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace FileMoles.Providers;

public class LocalFileSystemProvider : IStorageProvider, IDisposable
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileSystemProvider> _logger;
    private bool _disposed;

    public string ProviderId => "local";

    public LocalFileSystemProvider(string rootPath, ILogger<LocalFileSystemProvider> logger)
    {
        if (string.IsNullOrEmpty(rootPath))
            throw new ArgumentException("Root path cannot be empty", nameof(rootPath));

        _rootPath = PathUtils.NormalizePath(rootPath);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!Directory.Exists(_rootPath))
        {
            throw new DirectoryNotFoundException($"Root directory does not exist: {_rootPath}");
        }
    }

    public async Task<IEnumerable<FileSystemItem>> ListItemsAsync(string path)
    {
        ThrowIfDisposed();
        var normalizedPath = NormalizePath(path);
        var items = new List<FileSystemItem>();

        try
        {
            await Task.Run(() =>
            {
                var directory = new DirectoryInfo(normalizedPath);

                // Get directories
                foreach (var dir in directory.GetDirectories())
                {
                    if (ShouldSkipItem(dir))
                        continue;

                    items.Add(CreateFileSystemItem(dir));
                }

                // Get files
                foreach (var file in directory.GetFiles())
                {
                    if (ShouldSkipItem(file))
                        continue;

                    items.Add(CreateFileSystemItem(file));
                }
            });

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing items in {Path}", path);
            throw new StorageOperationException("ListItems", ex.Message);
        }
    }

    public async Task<FileSystemItem> GetItemAsync(string path)
    {
        ThrowIfDisposed();
        var normalizedPath = NormalizePath(path);

        try
        {
            return await Task.Run(() =>
            {
                if (Directory.Exists(normalizedPath))
                    return CreateFileSystemItem(new DirectoryInfo(normalizedPath));
                else if (File.Exists(normalizedPath))
                    return CreateFileSystemItem(new FileInfo(normalizedPath));

                throw new FileNotFoundException($"Path not found: {path}");
            });
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            _logger.LogError(ex, "Error getting item {Path}", path);
            throw new StorageOperationException("GetItem", ex.Message);
        }
    }

    private FileSystemItem CreateFileSystemItem(FileSystemInfo info)
    {
        var relativePath = PathUtils.GetRelativePath(_rootPath, info.FullName);

        var item = new FileSystemItem
        {
            Name = info.Name,
            FullPath = relativePath,
            IsDirectory = info is DirectoryInfo,
            CreationTime = info.CreationTimeUtc,
            LastAccessTime = info.LastAccessTimeUtc,
            LastWriteTime = info.LastWriteTimeUtc,
            Extension = info.Extension,
        };

        if (info is FileInfo fileInfo)
        {
            item.Size = fileInfo.Length;
        }

        // Cross-platform attributes handling
        item.Attributes = GetPlatformIndependentAttributes(info);

        return item;
    }

    private FileAttributes GetPlatformIndependentAttributes(FileSystemInfo info)
    {
        var attributes = FileAttributes.Normal;

        try
        {
            if (info.Attributes.HasFlag(FileAttributes.Hidden))
                attributes |= FileAttributes.Hidden;

            if (info.Attributes.HasFlag(FileAttributes.ReadOnly))
                attributes |= FileAttributes.ReadOnly;

            // Unix-specific hidden files (starting with .)
            if (info.Name.StartsWith("."))
                attributes |= FileAttributes.Hidden;

            if (info is DirectoryInfo)
                attributes |= FileAttributes.Directory;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting attributes for {Path}", info.FullName);
        }

        return attributes;
    }

    private bool ShouldSkipItem(FileSystemInfo info)
    {
        // Skip system files on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (info.Attributes.HasFlag(FileAttributes.System))
                return true;
        }

        // Skip certain Unix hidden files
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (info.Name.StartsWith(".") &&
                new[] { ".DS_Store", ".Trash", ".git" }.Contains(info.Name))
                return true;
        }

        return false;
    }

    private string NormalizePath(string path)
    {
        var normalizedPath = PathUtils.NormalizePath(path);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalizedPath.TrimStart('/')));

        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidPathException(path, "Path is outside of root directory");

        return fullPath;
    }

    public async Task<Stream> OpenReadAsync(string path)
    {
        ThrowIfDisposed();
        var normalizedPath = NormalizePath(path);

        try
        {
            // Use FileShare.Read to allow concurrent read access
            return await Task.Run(() => new FileStream(
                normalizedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening file for reading {Path}", path);
            throw new StorageOperationException("OpenRead", ex.Message);
        }
    }

    public async Task<Stream> OpenWriteAsync(string path)
    {
        ThrowIfDisposed();
        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath);

        try
        {
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Use FileShare.None for exclusive write access
            return await Task.Run(() => new FileStream(
                normalizedPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening file for writing {Path}", path);
            throw new StorageOperationException("OpenWrite", ex.Message);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LocalFileSystemProvider));
    }
}