using System.Timers;

namespace FileMoles;

internal class Debouncer<T>
{
    private readonly System.Timers.Timer _timer;
    private readonly Dictionary<string, T> _debouncedItems;
    private readonly Action<IEnumerable<T>> _action;

    public Debouncer(int interval, Action<IEnumerable<T>> action)
    {
        _timer = new System.Timers.Timer(interval);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = false;
        _debouncedItems = new Dictionary<string, T>();
        _action = action;
    }

    public void Debounce(string key, T item)
    {
        _debouncedItems[key] = item;
        _timer.Stop();
        _timer.Start();
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        _action(_debouncedItems.Values);
        _debouncedItems.Clear();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
