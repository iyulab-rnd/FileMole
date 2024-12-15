using FileMoles.Core.Models;
using Microsoft.Extensions.Hosting;

namespace FileMoles.Core.Interfaces;

public interface IFileSystemMonitor : IHostedService
{
    Task StartMonitoringAsync(string providerId, string path);
    Task StopMonitoringAsync(string path);
    Task<bool> IsMonitoringAsync(string path);
    Task<IEnumerable<string>> GetMonitoredPathsAsync();

    event EventHandler<FileSystemEventArgs> FileChanged;
    event EventHandler<FileSystemEventArgs> FileCreated;
    event EventHandler<FileSystemEventArgs> FileDeleted;
    event EventHandler<RenamedEventArgs> FileRenamed;

    // 추가된 이벤트들
    event EventHandler<FileSystemErrorEventArgs> MonitoringError;
    event EventHandler<FileSystemEventArgs> AccessDenied;
    event EventHandler<FileSystemEventArgs> DirectoryChanged;

    Task<FileSystemMonitorStatistics> GetStatisticsAsync();
    Task ResetStatisticsAsync();
}

public class FileSystemMonitorStatistics
{
    public int TotalChanges { get; set; }
    public int FileCreations { get; set; }
    public int FileDeletions { get; set; }
    public int FileModifications { get; set; }
    public int DirectoryChanges { get; set; }
    public int ErrorCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastChangeTime { get; set; }
    public IDictionary<string, int> ChangesByProvider { get; set; }
}

public class FileSystemErrorEventArgs : EventArgs
{
    public string Path { get; }
    public Exception Error { get; }
    public string ProviderId { get; }

    public FileSystemErrorEventArgs(string providerId, string path, Exception error)
    {
        ProviderId = providerId;
        Path = path;
        Error = error;
    }
}

public interface IChangeNotificationService
{
    Task NotifyChangesAsync(string providerId, IEnumerable<FileSystemChange> changes);
    Task<IEnumerable<FileSystemChange>> GetPendingChangesAsync(string providerId);
    Task AcknowledgeChangesAsync(string providerId, IEnumerable<string> changeIds);

    event EventHandler<FileSystemChangeEventArgs> ChangesAvailable;
}

public class FileSystemChange
{
    public string Id { get; set; }
    public string ProviderId { get; set; }
    public string Path { get; set; }
    public FileSystemChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; }
    public string OldPath { get; set; }  // For rename operations
    public FileSystemItem Item { get; set; }
}

public enum FileSystemChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed,
    SecurityChanged,
    AttributesChanged
}
