using FileMoles.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace FileMoles.Monitoring;

public enum FileSystemEventType
{
    Created,
    Changed,
    Deleted,
    Renamed,
    SecurityChanged
}

public class FileSystemEvent
{
    public string ProviderId { get; set; }
    public string Path { get; set; }
    public FileSystemEventType Type { get; set; }
    public DateTime Timestamp { get; set; }
}

public class RenamedFileSystemEvent : FileSystemEvent
{
    public string OldPath { get; set; }
    public string NewPath { get; set; }
}

public class FileSystemMonitor : IFileSystemMonitor, IHostedService, IDisposable
{
    private readonly IFileSystemCache _cache;
    private readonly ILogger<FileSystemMonitor> _logger;
    private readonly MonitoringOptions _options;
    private readonly ConcurrentDictionary<string, IFileSystemWatcher> _watchers;
    private readonly BlockingCollection<FileSystemEvent> _eventQueue;
    private readonly CancellationTokenSource _processingCts;
    private readonly Task _processingTask;
    private readonly FileSystemMonitorStatistics _statistics;
    private bool _disposed;

    public event EventHandler<FileSystemEventArgs> FileChanged;
    public event EventHandler<FileSystemEventArgs> FileCreated;
    public event EventHandler<FileSystemEventArgs> FileDeleted;
    public event EventHandler<RenamedEventArgs> FileRenamed;
    public event EventHandler<FileSystemEventArgs> DirectoryChanged;
    public event EventHandler<FileSystemErrorEventArgs> MonitoringError;
    public event EventHandler<FileSystemEventArgs> AccessDenied;

    public FileSystemMonitor(
        IFileSystemCache cache,
        ILogger<FileSystemMonitor> logger,
        IOptions<MonitoringOptions> options)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        _watchers = new ConcurrentDictionary<string, IFileSystemWatcher>();
        _eventQueue = new BlockingCollection<FileSystemEvent>();
        _processingCts = new CancellationTokenSource();
        _statistics = new FileSystemMonitorStatistics
        {
            StartTime = DateTime.UtcNow,
            ChangesByProvider = new ConcurrentDictionary<string, int>()
        };

        _processingTask = Task.Run(ProcessEventsAsync);
    }

    public async Task StartMonitoringAsync(string providerId, string path)
    {
        ThrowIfDisposed();
        var watcherKey = $"{providerId}:{path}";

        if (_watchers.ContainsKey(watcherKey))
        {
            _logger.LogWarning("Already monitoring {ProviderId}:{Path}", providerId, path);
            return;
        }

        try
        {
            var watcher = CreateFileSystemWatcher(providerId, path);
            if (_watchers.TryAdd(watcherKey, watcher))
            {
                await watcher.StartAsync();
                _logger.LogInformation("Started monitoring {ProviderId}:{Path}", providerId, path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start monitoring {ProviderId}:{Path}", providerId, path);
            throw;
        }
    }

    public async Task StopMonitoringAsync(string path)
    {
        ThrowIfDisposed();
        var watchersToRemove = _watchers.Where(w => w.Value.Path == path).ToList();

        foreach (var kvp in watchersToRemove)
        {
            if (_watchers.TryRemove(kvp.Key, out var watcher))
            {
                await watcher.StopAsync();
                watcher.Dispose();
            }
        }
    }

    public Task<bool> IsMonitoringAsync(string path)
    {
        ThrowIfDisposed();
        return Task.FromResult(_watchers.Any(w => w.Value.Path == path));
    }

    public Task<IEnumerable<string>> GetMonitoredPathsAsync()
    {
        ThrowIfDisposed();
        return Task.FromResult(_watchers.Values.Select(w => w.Path).Distinct());
    }

    public Task<FileSystemMonitorStatistics> GetStatisticsAsync()
    {
        ThrowIfDisposed();
        return Task.FromResult(_statistics);
    }

    public Task ResetStatisticsAsync()
    {
        ThrowIfDisposed();
        _statistics.Reset();
        return Task.CompletedTask;
    }

    private IFileSystemWatcher CreateFileSystemWatcher(string providerId, string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = _options.NotifyFilters,
            IncludeSubdirectories = _options.IncludeSubdirectories,
            EnableRaisingEvents = false
        };

        watcher.Changed += (s, e) => HandleFileSystemEvent(providerId, e, FileSystemEventType.Changed);
        watcher.Created += (s, e) => HandleFileSystemEvent(providerId, e, FileSystemEventType.Created);
        watcher.Deleted += (s, e) => HandleFileSystemEvent(providerId, e, FileSystemEventType.Deleted);
        watcher.Renamed += (s, e) => HandleRenamedEvent(providerId, e);
        watcher.Error += (s, e) => HandleWatcherError(providerId, path, e);

        return new FileSystemWatcherAdapter(watcher, _logger);
    }

    private void HandleFileSystemEvent(string providerId, FileSystemEventArgs e, FileSystemEventType eventType)
    {
        var evt = new FileSystemEvent
        {
            ProviderId = providerId,
            Path = e.FullPath,
            Type = eventType,
            Timestamp = DateTime.UtcNow
        };

        if (!_eventQueue.TryAdd(evt))
        {
            _logger.LogWarning("Event queue is full, event dropped for {Path}", e.FullPath);
        }

        UpdateStatistics(providerId, eventType);
    }

    private void HandleRenamedEvent(string providerId, RenamedEventArgs e)
    {
        var evt = new RenamedFileSystemEvent
        {
            ProviderId = providerId,
            Path = e.FullPath,
            OldPath = e.OldFullPath,
            Type = FileSystemEventType.Renamed,
            Timestamp = DateTime.UtcNow
        };

        if (!_eventQueue.TryAdd(evt))
        {
            _logger.LogWarning("Event queue is full, rename event dropped for {Path}", e.FullPath);
        }

        UpdateStatistics(providerId, FileSystemEventType.Renamed);
    }

    private void HandleWatcherError(string providerId, string path, ErrorEventArgs e)
    {
        _statistics.ErrorCount++;
        _logger.LogError(e.GetException(), "File system watcher error for {ProviderId}:{Path}",
            providerId, path);

        MonitoringError?.Invoke(this, new FileSystemErrorEventArgs(providerId, path, e.GetException()));
    }

    private async Task ProcessEventsAsync()
    {
        while (!_processingCts.Token.IsCancellationRequested)
        {
            try
            {
                var evt = _eventQueue.Take(_processingCts.Token);
                await ProcessEventAsync(evt);
            }
            catch (OperationCanceledException) when (_processingCts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file system event");
            }
        }
    }

    private async Task ProcessEventAsync(FileSystemEvent evt)
    {
        try
        {
            await _cache.InvalidateCacheAsync(evt.ProviderId, evt.Path);

            switch (evt.Type)
            {
                case FileSystemEventType.Created:
                    FileCreated?.Invoke(this, new FileSystemEventArgs(evt.Path, evt.ProviderId));
                    break;
                case FileSystemEventType.Changed:
                    FileChanged?.Invoke(this, new FileSystemEventArgs(evt.Path, evt.ProviderId));
                    break;
                case FileSystemEventType.Deleted:
                    FileDeleted?.Invoke(this, new FileSystemEventArgs(evt.Path, evt.ProviderId));
                    break;
                case FileSystemEventType.Renamed:
                    if (evt is RenamedFileSystemEvent renameEvt)
                    {
                        await _cache.InvalidateCacheAsync(evt.ProviderId, renameEvt.OldPath);
                        FileRenamed?.Invoke(this, new RenamedEventArgs(renameEvt.OldPath, renameEvt.NewPath));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing event {EventType} for {Path}", evt.Type, evt.Path);
            MonitoringError?.Invoke(this, new FileSystemErrorEventArgs(evt.ProviderId, evt.Path, ex));
        }
    }

    private void UpdateStatistics(string providerId, FileSystemEventType eventType)
    {
        _statistics.TotalChanges++;
        _statistics.LastChangeTime = DateTime.UtcNow;

        switch (eventType)
        {
            case FileSystemEventType.Created:
                _statistics.FileCreations++;
                break;
            case FileSystemEventType.Deleted:
                _statistics.FileDeletions++;
                break;
            case FileSystemEventType.Changed:
                _statistics.FileModifications++;
                break;
        }

        _statistics.ChangesByProvider.AddOrUpdate(
            providerId,
            1,
            (_, count) => count + 1);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Start();
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _processingCts.Cancel();
        foreach (var watcher in _watchers.Values)
        {
            await watcher.StopAsync();
        }

        try
        {
            await Task.WhenAny(
                _processingTask,
                Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)
            );
        }
        catch (OperationCanceledException)
        {
            // Expected when shutdown
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _processingCts.Cancel();
            _processingCts.Dispose();
            _eventQueue.Dispose();

            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }
            _watchers.Clear();

            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileSystemMonitor));
    }
}

public class FileSystemWatcherAdapter : IFileSystemWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly ILogger _logger;

    public string Path => _watcher.Path;

    public FileSystemWatcherAdapter(FileSystemWatcher watcher, ILogger logger)
    {
        _watcher = watcher;
        _logger = logger;
    }

    public void Start()
    {
        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting file system watcher for {Path}", _watcher.Path);
            throw;
        }
    }

    public Task StartAsync()
    {
        Start();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _watcher.EnableRaisingEvents = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}

public class FileSystemEventArgs : EventArgs
{
    public string Path { get; }
    public string ProviderId { get; }

    public FileSystemEventArgs(string path, string providerId)
    {
        Path = path;
        ProviderId = providerId;
    }
}

public class RenamedEventArgs : FileSystemEventArgs
{
    public string OldPath { get; }
    public string NewPath { get; }

    public RenamedEventArgs(string oldPath, string newPath)
        : base(newPath, null)
    {
        OldPath = oldPath;
        NewPath = newPath;
    }
}