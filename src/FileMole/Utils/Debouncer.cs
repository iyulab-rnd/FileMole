using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FileMole.Utils;

public class Debouncer
{
    private readonly ConcurrentDictionary<object, CancellationTokenSource> _pending = new ConcurrentDictionary<object, CancellationTokenSource>();
    private readonly TimeSpan _debouncePeriod;

    public Debouncer(TimeSpan debouncePeriod)
    {
        _debouncePeriod = debouncePeriod;
    }

    public async Task DebounceAsync(object key, Func<Task> action)
    {
        var cts = new CancellationTokenSource();
        var delayTask = Task.Delay(_debouncePeriod, cts.Token);

        if (_pending.TryGetValue(key, out var existingCts))
        {
            existingCts.Cancel();
        }

        _pending[key] = cts;

        try
        {
            await delayTask;
            await action();
        }
        catch (TaskCanceledException)
        {
            // Ignored, as this is expected when debouncing
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }
}