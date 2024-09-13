using System.Collections.Concurrent;

namespace FileMoles.Internal;

internal class EventDebouncer<T> : IDisposable, IAsyncDisposable
{
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<string, T> _debouncedItems = new();
    private readonly Func<IEnumerable<T>, Task> _asyncAction;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly int _interval;
    private bool _disposed = false;

    public EventDebouncer(int interval, Func<IEnumerable<T>, Task> asyncAction)
    {
        _interval = interval;
        _timer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _asyncAction = asyncAction;
    }

    public async Task DebounceAsync(string key, T item)
    {
        _debouncedItems[key] = item;
        await _semaphore.WaitAsync();
        try
        {
            _timer.Change(_interval, Timeout.Infinite);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async void OnTimerElapsed(object? state)
    {
        await _semaphore.WaitAsync();
        try
        {
            var items = _debouncedItems.Values.ToList();
            _debouncedItems.Clear();
            await _asyncAction(items);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _timer.Dispose();
            _semaphore.Dispose();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (!_disposed)
        {
            if (_timer != null)
            {
                await _timer.DisposeAsync();
            }

            // Only dispose semaphore once
            _semaphore?.Dispose();

            _disposed = true;
        }
    }
}
