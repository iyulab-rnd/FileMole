using System.Collections.Concurrent;

namespace FileMoles;

public static class FileSafe
{
    private static readonly ConcurrentDictionary<string, string> _contentStore = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new();
    private static bool _isDisposed = false;

    [System.Diagnostics.DebuggerStepThrough]
    public static async Task WriteAllTextWithRetryAsync(string path, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FileSafe));

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

    public static async void WriteAllTextWithRetry(string path, string contents, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        await WriteAllTextWithRetryAsync(path, contents, maxRetries, delayMilliseconds, cancellationToken);
    }

    [System.Diagnostics.DebuggerStepThrough]
    public static async Task DeleteRetryAsync(string path, int maxRetries = 3, int delayMilliseconds = 100, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(FileSafe));

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