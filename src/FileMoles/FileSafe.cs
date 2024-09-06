using System.Collections.Concurrent;
using System.Diagnostics;

namespace FileMoles;

public static class FileSafe
{
    private static readonly ConcurrentDictionary<string, string> _contentStore = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new();
    private static bool _isDisposed = false;

    [DebuggerStepThrough]
    public static async Task WriteAllTextWithRetryAsync(string path, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(FileSafe));

        SemaphoreSlim semaphore = _pathLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        try
        {
            if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new TimeoutException($"Timeout waiting for lock on file: {path}");
            }

            try
            {
                _contentStore[path] = contents;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string contentToWrite = _contentStore[path];
                        await File.WriteAllTextAsync(path, contentToWrite, cancellationToken);

                        if (_contentStore.TryGetValue(path, out var latestContent) && latestContent == contentToWrite)
                        {
                            _contentStore.TryRemove(path, out _);
                            return;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (i < maxRetries - 1 && (ex is IOException || ex is UnauthorizedAccessException))
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
        catch (OperationCanceledException)
        {
            _contentStore.TryRemove(path, out _);
            throw;
        }
    }

    [DebuggerStepThrough]
    public static async void WriteAllTextWithRetry(string path, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        await WriteAllTextWithRetryAsync(path, contents, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static async Task DeleteRetryAsync(string path, int maxRetries = 5, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(FileSafe));

        SemaphoreSlim semaphore = _pathLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        try
        {
            if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
            {
                throw new TimeoutException($"Timeout waiting for lock on file: {path}");
            }

            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                        else if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true); // true to delete recursively
                        }
                        else
                        {
                            return; // 경로가 존재하지 않으면 삭제할 필요 없음
                        }

                        return; // 성공적으로 삭제 시 리턴
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (i < maxRetries - 1 && (ex is IOException || ex is UnauthorizedAccessException))
                    {
                        await Task.Delay(delayMilliseconds, cancellationToken);
                    }
                }
                throw new IOException($"Failed to delete after {maxRetries} attempts: {path}");
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public static async void DeleteRetry(string path, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        await DeleteRetryAsync(path, maxRetries, delayMilliseconds, cancellationToken);
    }

    [DebuggerStepThrough]
    public static async Task CopyWithRetryAsync(string sourcePath, string destinationPath, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, nameof(FileSafe));

        if (File.Exists(sourcePath))
        {
            await CopyFileWithRetryAsync(sourcePath, destinationPath, maxRetries, delayMilliseconds, cancellationToken);
        }
        else if (Directory.Exists(sourcePath))
        {
            await CopyDirectoryWithRetryAsync(sourcePath, destinationPath, maxRetries, delayMilliseconds, cancellationToken);
        }
        else
        {
            throw new FileNotFoundException($"Source path not found: {sourcePath}");
        }
    }

    [DebuggerStepThrough]
    private static async Task CopyFileWithRetryAsync(string sourcePath, string destinationPath, int maxRetries, int delayMilliseconds, CancellationToken cancellationToken)
    {
        SemaphoreSlim sourceSemaphore = _pathLocks.GetOrAdd(sourcePath, _ => new SemaphoreSlim(1, 1));
        SemaphoreSlim destSemaphore = _pathLocks.GetOrAdd(destinationPath, _ => new SemaphoreSlim(1, 1));

        try
        {
            using FileStream sourceStream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            Task sourceWaitTask = sourceSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            Task destWaitTask = destSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

            await Task.WhenAll(sourceWaitTask, destWaitTask);

            if (!sourceWaitTask.IsCompletedSuccessfully || !destWaitTask.IsCompletedSuccessfully)
            {
                throw new TimeoutException($"Timeout waiting for lock on files: {sourcePath} or {destinationPath}");
            }

            try
            {
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        using FileStream destStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
                        await sourceStream.CopyToAsync(destStream, 81920, cancellationToken);

                        return; // 성공적으로 복사 완료
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (i < maxRetries - 1 && (ex is IOException || ex is UnauthorizedAccessException))
                    {
                        await Task.Delay(delayMilliseconds, cancellationToken);
                    }
                }
                throw new IOException($"Failed to copy file after {maxRetries} attempts: {sourcePath} to {destinationPath}");
            }
            finally
            {
                sourceSemaphore.Release();
                destSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    [DebuggerStepThrough]
    private static async Task CopyDirectoryWithRetryAsync(string sourceDir, string destDir, int maxRetries, int delayMilliseconds, CancellationToken cancellationToken)
    {
        var dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
        }

        DirectoryInfo[] dirs = dir.GetDirectories();

        // 대상 디렉토리가 없으면 생성
        Directory.CreateDirectory(destDir);

        // 파일 복사
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destDir, file.Name);
            await CopyFileWithRetryAsync(file.FullName, targetFilePath, maxRetries, delayMilliseconds, cancellationToken);
        }

        // 하위 디렉토리 재귀적으로 복사
        foreach (DirectoryInfo subDir in dirs)
        {
            string newDestinationDir = Path.Combine(destDir, subDir.Name);
            await CopyDirectoryWithRetryAsync(subDir.FullName, newDestinationDir, maxRetries, delayMilliseconds, cancellationToken);
        }
    }

    [DebuggerStepThrough]
    public static async void CopyWithRetry(string sourcePath, string destinationPath, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        await CopyWithRetryAsync(sourcePath, destinationPath, maxRetries, delayMilliseconds, cancellationToken);
    }

    public static void Dispose()
    {
        if (!_isDisposed)
        {
            foreach (var semaphore in _pathLocks.Values)
            {
                semaphore.Dispose();
            }
            _pathLocks.Clear();
            _contentStore.Clear();
            _isDisposed = true;
        }
    }

}