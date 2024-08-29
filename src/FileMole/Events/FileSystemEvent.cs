using System.IO;
namespace FileMole.Events;

public class FileSystemEvent
{
    public WatcherChangeTypes ChangeType { get; }
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public bool IsDirectory { get; }

    public FileSystemEvent(WatcherChangeTypes changeType, string fullPath, string? oldFullPath = null)
    {
        ChangeType = changeType;
        FullPath = fullPath;
        OldFullPath = oldFullPath;
        IsDirectory = Directory.Exists(fullPath);
    }
}