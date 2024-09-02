using System.Timers;

namespace FileMoles;

internal class Debouncer<T> : IDisposable
{
    private readonly System.Timers.Timer _timer;
    private readonly Dictionary<string, T> _debouncedItems;
    private readonly Func<IEnumerable<T>, Task> _asyncAction;

    public Debouncer(int interval, Func<IEnumerable<T>, Task> asyncAction)
    {
        _timer = new System.Timers.Timer(interval);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = false;
        _debouncedItems = new Dictionary<string, T>();
        _asyncAction = asyncAction;
    }

    public void Debounce(string key, T item)
    {
        _debouncedItems[key] = item;
        _timer.Stop();
        _timer.Start();
    }

    public Task DebounceAsync(string key, T item)
    {
        Debounce(key, item);
        var tcs = new TaskCompletionSource<bool>();
        _timer.Elapsed += (sender, args) => tcs.TrySetResult(true);
        return tcs.Task;
    }

    private async void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        await _asyncAction(_debouncedItems.Values);
        _debouncedItems.Clear();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}