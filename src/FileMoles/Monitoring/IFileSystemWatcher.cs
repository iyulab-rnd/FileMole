using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FileMoles.Monitoring;

public interface IFileSystemWatcher : IDisposable
{
    event FileSystemEventHandler Changed;
    event FileSystemEventHandler Created;
    event FileSystemEventHandler Deleted;
    event RenamedEventHandler Renamed;
    event ErrorEventHandler Error;

    void Start();
    Task StopAsync();
}

// Windows 구현
public class WindowsFileSystemWatcher : IFileSystemWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly MonitoringOptions _options;

    public event FileSystemEventHandler Changed
    {
        add => _watcher.Changed += value;
        remove => _watcher.Changed -= value;
    }
    public event FileSystemEventHandler Created
    {
        add => _watcher.Created += value;
        remove => _watcher.Created -= value;
    }
    public event FileSystemEventHandler Deleted
    {
        add => _watcher.Deleted += value;
        remove => _watcher.Deleted -= value;
    }
    public event RenamedEventHandler Renamed
    {
        add => _watcher.Renamed += value;
        remove => _watcher.Renamed -= value;
    }
    public event ErrorEventHandler Error
    {
        add => _watcher.Error += value;
        remove => _watcher.Error -= value;
    }

    public WindowsFileSystemWatcher(string path, MonitoringOptions options)
    {
        _options = options;
        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = options.NotifyFilters,
            IncludeSubdirectories = options.IncludeSubdirectories,
            InternalBufferSize = 65536  // 64KB buffer
        };
    }

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
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

// Linux 구현
public class LinuxFileSystemWatcher : IFileSystemWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly MonitoringOptions _options;
    private readonly ILogger<LinuxFileSystemWatcher> _logger;

    public event FileSystemEventHandler Changed;
    public event FileSystemEventHandler Created;
    public event FileSystemEventHandler Deleted;
    public event RenamedEventHandler Renamed;
    public event ErrorEventHandler Error;

    public LinuxFileSystemWatcher(string path, MonitoringOptions options)
    {
        _options = options;
        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = options.NotifyFilters,
            IncludeSubdirectories = options.IncludeSubdirectories
        };

        // inotify 이벤트 매핑
        _watcher.Changed += (s, e) => Changed?.Invoke(s, e);
        _watcher.Created += (s, e) => Created?.Invoke(s, e);
        _watcher.Deleted += (s, e) => Deleted?.Invoke(s, e);
        _watcher.Renamed += (s, e) => Renamed?.Invoke(s, e);
        _watcher.Error += (s, e) => Error?.Invoke(s, e);
    }

    public void Start()
    {
        try
        {
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex.Message.Contains("inotify"))
        {
            _logger.LogError(ex, "Failed to initialize inotify watcher. Check system limits.");
            throw new PlatformNotSupportedException(
                "Failed to initialize file system monitoring. Check inotify limits.", ex);
        }
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

// macOS 구현 계속
public class MacOSFileSystemWatcher : IFileSystemWatcher
{
    private readonly FileSystemWatcher _watcher;
    private readonly MonitoringOptions _options;
    private readonly ILogger<MacOSFileSystemWatcher> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastEvents;

    public event FileSystemEventHandler Changed;
    public event FileSystemEventHandler Created;
    public event FileSystemEventHandler Deleted;
    public event RenamedEventHandler Renamed;
    public event ErrorEventHandler Error;

    public MacOSFileSystemWatcher(
        string path,
        MonitoringOptions options,
        ILogger<MacOSFileSystemWatcher> logger)
    {
        _options = options;
        _logger = logger;
        _lastEvents = new ConcurrentDictionary<string, DateTime>();

        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = options.NotifyFilters,
            IncludeSubdirectories = options.IncludeSubdirectories,
            // macOS FSEvents는 더 큰 버퍼 사이즈를 지원
            InternalBufferSize = 131072  // 128KB buffer
        };

        // macOS의 경우 이벤트 중복 발생 가능성이 높아 디바운싱 처리
        _watcher.Changed += HandleDebouncedEvent((s, e) => Changed?.Invoke(s, e));
        _watcher.Created += HandleDebouncedEvent((s, e) => Created?.Invoke(s, e));
        _watcher.Deleted += HandleDebouncedEvent((s, e) => Deleted?.Invoke(s, e));
        _watcher.Renamed += (s, e) => Renamed?.Invoke(s, e);  // Renamed는 디바운싱 불필요
        _watcher.Error += (s, e) => Error?.Invoke(s, e);
    }

    private FileSystemEventHandler HandleDebouncedEvent(FileSystemEventHandler handler)
    {
        return (sender, e) =>
        {
            var now = DateTime.UtcNow;
            var lastEvent = _lastEvents.GetOrAdd(e.FullPath, now);

            if ((now - lastEvent) > _options.DebounceDelay)
            {
                _lastEvents.TryUpdate(e.FullPath, now, lastEvent);
                handler?.Invoke(sender, e);
            }
        };
    }

    public void Start()
    {
        try
        {
            _watcher.EnableRaisingEvents = true;
            _logger.LogInformation("Started FSEvents monitoring for {Path}", _watcher.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FSEvents watcher");
            throw new PlatformNotSupportedException(
                "Failed to initialize file system monitoring on macOS.", ex);
        }
    }

    public Task StopAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _lastEvents.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _lastEvents.Clear();
    }
}

// 향상된 모니터링 이벤트 모델
public class FileSystemEventArgs : EventArgs
{
    public string Path { get; }
    public string FullPath { get; }
    public WatcherChangeTypes ChangeType { get; }
    public DateTime Timestamp { get; }
    public string ProviderId { get; }

    public FileSystemEventArgs(
        string providerId,
        WatcherChangeTypes changeType,
        string path,
        string fullPath)
    {
        ProviderId = providerId;
        ChangeType = changeType;
        Path = path;
        FullPath = fullPath;
        Timestamp = DateTime.UtcNow;
    }
}

public class FileSystemErrorEventArgs : EventArgs
{
    public string Path { get; }
    public Exception Error { get; }
    public string ProviderId { get; }
    public DateTime Timestamp { get; }
    public string ErrorDetails { get; }

    public FileSystemErrorEventArgs(
        string providerId,
        string path,
        Exception error,
        string details = null)
    {
        ProviderId = providerId;
        Path = path;
        Error = error;
        Timestamp = DateTime.UtcNow;
        ErrorDetails = details ?? error.Message;
    }
}

public class MonitoringOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public bool IncludeSubdirectories { get; set; } = true;
    public NotifyFilters NotifyFilters { get; set; } = NotifyFilters.LastWrite
        | NotifyFilters.FileName
        | NotifyFilters.DirectoryName;
    public int MaxBufferSize { get; set; } = 65536; // 기본 64KB
    public bool UsePolling { get; set; } = false; // 폴링 폴백 옵션
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxConcurrentWatchers { get; set; } = 100;
}

// 폴백 폴링 모니터 구현
public class PollingFileSystemWatcher : IFileSystemWatcher
{
    private readonly string _path;
    private readonly MonitoringOptions _options;
    private readonly ILogger<PollingFileSystemWatcher> _logger;
    private readonly ConcurrentDictionary<string, FileSystemInfo> _lastState;
    private readonly CancellationTokenSource _cts;
    private Task _pollingTask;

    public event FileSystemEventHandler Changed;
    public event FileSystemEventHandler Created;
    public event FileSystemEventHandler Deleted;
    public event RenamedEventHandler Renamed;
    public event ErrorEventHandler Error;

    public PollingFileSystemWatcher(
        string path,
        MonitoringOptions options,
        ILogger<PollingFileSystemWatcher> logger)
    {
        _path = path;
        _options = options;
        _logger = logger;
        _lastState = new ConcurrentDictionary<string, FileSystemInfo>();
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        // 초기 상태 스캔
        ScanDirectory(_path);

        // 폴링 태스크 시작
        _pollingTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(_options.PollingInterval, _cts.Token);
                await PollChangesAsync();
            }
        }, _cts.Token);
    }

    private async Task PollChangesAsync()
    {
        try
        {
            var currentState = new Dictionary<string, FileSystemInfo>();
            ScanDirectoryState(_path, currentState);

            // 변경 사항 감지
            foreach (var current in currentState)
            {
                if (!_lastState.TryGetValue(current.Key, out var previous))
                {
                    // 새로 생성됨
                    OnCreated(new FileSystemEventArgs(
                        WatcherChangeTypes.Created,
                        Path.GetDirectoryName(current.Key),
                        current.Key));
                }
                else if (IsFileChanged(previous, current.Value))
                {
                    // 변경됨
                    OnChanged(new FileSystemEventArgs(
                        WatcherChangeTypes.Changed,
                        Path.GetDirectoryName(current.Key),
                        current.Key));
                }
            }

            // 삭제 감지
            foreach (var previous in _lastState)
            {
                if (!currentState.ContainsKey(previous.Key))
                {
                    OnDeleted(new FileSystemEventArgs(
                        WatcherChangeTypes.Deleted,
                        Path.GetDirectoryName(previous.Key),
                        previous.Key));
                }
            }

            // 상태 업데이트
            _lastState.Clear();
            foreach (var item in currentState)
            {
                _lastState.TryAdd(item.Key, item.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during polling for {Path}", _path);
            OnError(new ErrorEventArgs(ex));
        }
    }

    private bool IsFileChanged(FileSystemInfo previous, FileSystemInfo current)
    {
        return previous.LastWriteTime != current.LastWriteTime ||
               previous.Length != current.Length;
    }

    protected virtual void OnChanged(FileSystemEventArgs e)
    {
        Changed?.Invoke(this, e);
    }

    protected virtual void OnCreated(FileSystemEventArgs e)
    {
        Created?.Invoke(this, e);
    }

    protected virtual void OnDeleted(FileSystemEventArgs e)
    {
        Deleted?.Invoke(this, e);
    }

    protected virtual void OnError(ErrorEventArgs e)
    {
        Error?.Invoke(this, e);
    }

    private void ScanDirectory(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            foreach (var file in directory.GetFiles())
            {
                _lastState.TryAdd(file.FullName, file);
            }

            if (_options.IncludeSubdirectories)
            {
                foreach (var dir in directory.GetDirectories())
                {
                    _lastState.TryAdd(dir.FullName, dir);
                    ScanDirectory(dir.FullName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory {Path}", path);
            OnError(new ErrorEventArgs(ex));
        }
    }

    private void ScanDirectoryState(string path, Dictionary<string, FileSystemInfo> state)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            foreach (var file in directory.GetFiles())
            {
                state[file.FullName] = file;
            }

            if (_options.IncludeSubdirectories)
            {
                foreach (var dir in directory.GetDirectories())
                {
                    state[dir.FullName] = dir;
                    ScanDirectoryState(dir.FullName, state);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory state for {Path}", path);
            OnError(new ErrorEventArgs(ex));
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_pollingTask != null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _lastState.Clear();
    }
}