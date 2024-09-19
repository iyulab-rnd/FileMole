using System.Collections.Concurrent;

public class Debouncer<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, (CancellationTokenSource Cts, TValue Value)> _items = new();
    private readonly TimeSpan _debounceTime;

    public Debouncer(TimeSpan debounceTime)
    {
        _debounceTime = debounceTime;
    }

    public async Task<TValue> DebounceAsync(TKey key, TValue value, Func<TValue, Task> action, CancellationToken cancellationToken = default)
    {
        if (_items.TryGetValue(key, out var existing))
        {
            existing.Cts.Cancel();
            existing.Cts.Dispose();
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var item = (Cts: linkedCts, Value: value);
        _items[key] = item;

        try
        {
            await Task.Delay(_debounceTime, linkedCts.Token);
            await action(value);
            return value;
        }
        catch (OperationCanceledException)
        {
            // Debounce was cancelled, ignore
            return value;
        }
        finally
        {
            _items.TryRemove(key, out _);
            linkedCts.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var (cts, _) in _items.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _items.Clear();
    }
}