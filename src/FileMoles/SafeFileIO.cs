﻿using System.Collections.Concurrent;

namespace FileMoles;

public static class SafeFileIO
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new();

    public static async Task WriteAllTextAsync(string path, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        var semaphore = _pathLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(path, contents, cancellationToken);
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
            }
            throw new IOException($"Failed to write to file after {maxRetries} attempts: {path}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task DeleteAsync(string path, int maxRetries = 5, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        var semaphore = _pathLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task CopyAsync(string sourcePath, string destinationPath, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        var sourceSemaphore = _pathLocks.GetOrAdd(sourcePath, _ => new SemaphoreSlim(1, 1));
        var destSemaphore = _pathLocks.GetOrAdd(destinationPath, _ => new SemaphoreSlim(1, 1));

        await sourceSemaphore.WaitAsync(cancellationToken);
        try
        {
            await destSemaphore.WaitAsync(cancellationToken);
            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        if (File.Exists(sourcePath))
                        {
                            File.Copy(sourcePath, destinationPath, true);
                        }
                        else if (Directory.Exists(sourcePath))
                        {
                            CopyDirectory(sourcePath, destinationPath);
                        }
                        else
                        {
                            throw new FileNotFoundException($"Source path not found: {sourcePath}");
                        }
                        return;
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        await Task.Delay(delayMilliseconds, cancellationToken);
                    }
                }
                throw new IOException($"Failed to copy after {maxRetries} attempts: {sourcePath} to {destinationPath}");
            }
            finally
            {
                destSemaphore.Release();
            }
        }
        finally
        {
            sourceSemaphore.Release();
        }
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(destinationPath))
        {
            Directory.CreateDirectory(destinationPath);
        }

        foreach (string file in Directory.GetFiles(sourcePath))
        {
            string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (string dir in Directory.GetDirectories(sourcePath))
        {
            string destDir = Path.Combine(destinationPath, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    public static async Task AppendAllTextAsync(string filePath, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        var semaphore = _pathLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var streamWriter = new StreamWriter(filePath, append: true);
                    await streamWriter.WriteAsync(contents.AsMemory(), cancellationToken);
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
            }
            throw new IOException($"Failed to append to file after {maxRetries} attempts: {filePath}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static async Task MoveAsync(string sourceFileName, string destFileName, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        var sourceSemaphore = _pathLocks.GetOrAdd(sourceFileName, _ => new SemaphoreSlim(1, 1));
        var destSemaphore = _pathLocks.GetOrAdd(destFileName, _ => new SemaphoreSlim(1, 1));

        await sourceSemaphore.WaitAsync(cancellationToken);
        try
        {
            await destSemaphore.WaitAsync(cancellationToken);
            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        if (File.Exists(sourceFileName))
                        {
                            File.Move(sourceFileName, destFileName);
                            return;
                        }
                        else
                        {
                            throw new FileNotFoundException($"Source file not found: {sourceFileName}");
                        }
                    }
                    catch (IOException) when (i < maxRetries - 1)
                    {
                        await Task.Delay(delayMilliseconds, cancellationToken);
                    }
                }
                throw new IOException($"Failed to move file after {maxRetries} attempts: {sourceFileName} to {destFileName}");
            }
            finally
            {
                destSemaphore.Release();
            }
        }
        finally
        {
            sourceSemaphore.Release();
        }
    }
}