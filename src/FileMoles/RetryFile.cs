using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace FileMoles;

public static class RetryFile
{
    private static async Task<T> ExecuteWithRetry<T>(Func<Task<T>> action, string filePath, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        bool wasHidden = false;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                T result = await action();

                if (wasHidden)
                {
                    File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.Hidden);
                }

                return result;
            }
            catch (UnauthorizedAccessException) when (i < maxRetries - 1)
            {
                if (File.Exists(filePath) && (File.GetAttributes(filePath) & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    wasHidden = true;
                    File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.Hidden);
                }
                else
                {
                    throw;  // 숨김 파일이 아니라면 예외를 다시 던집니다.
                }
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                await Task.Delay(delayMilliseconds * (i + 1), cancellationToken);
            }
        }

        throw new IOException($"Failed to execute operation after {maxRetries} attempts.");
    }

    private static bool IsHiddenFile(string path)
    {
        return Path.GetFileName(path).StartsWith('.');
    }

    private static void SetHiddenAttribute(string path)
    {
        if (IsHiddenFile(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
    }

    public static Task WriteAllTextAsync(string path, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            await File.WriteAllTextAsync(path, contents, cancellationToken);
            SetHiddenAttribute(path);
            return await Task.FromResult(true);
        }, path, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static Task DeleteAsync(string path, int maxRetries = 5, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            return await Task.FromResult(true);
        }, path, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static Task CopyAsync(string sourcePath, string destinationPath, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
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

            SetHiddenAttribute(destinationPath);
            return await Task.FromResult(true);
        }, sourcePath, maxRetries, delayMilliseconds, cancellationToken);
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
            SetHiddenAttribute(destFile);
        }

        foreach (string dir in Directory.GetDirectories(sourcePath))
        {
            string destDir = Path.Combine(destinationPath, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    public static Task AppendAllTextAsync(string filePath, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            using var streamWriter = new StreamWriter(filePath, append: true);
            await streamWriter.WriteAsync(contents.AsMemory(), cancellationToken);
            SetHiddenAttribute(filePath);
            return await Task.FromResult(true);
        }, filePath, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static Task MoveAsync(string sourceFileName, string destFileName, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            if (File.Exists(sourceFileName))
            {
                File.Move(sourceFileName, destFileName);
                SetHiddenAttribute(destFileName);
                return await Task.FromResult(true);
            }
            else
            {
                throw new FileNotFoundException($"Source file not found: {sourceFileName}");
            }
        }, sourceFileName, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static Task WriteAllLinesAsync(string path, IEnumerable<string> contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            await File.WriteAllLinesAsync(path, contents, cancellationToken);
            SetHiddenAttribute(path);
            return await Task.FromResult(true);
        }, path, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static Task<string> ReadAllTextAsync(string path, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return text;
        }, path, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static Task<string[]> ReadAllLinesAsync(string path, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetry(async () =>
        {
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            return lines;
        }, path, maxRetries, delayMilliseconds, cancellationToken);
    }
}