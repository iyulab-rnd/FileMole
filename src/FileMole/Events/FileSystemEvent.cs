using System.IO;

namespace FileMole.Events;

public class FileSystemEvent
{
    public WatcherChangeTypes ChangeType { get; set; }
    public string FullPath { get; set; }

    public FileSystemEvent(WatcherChangeTypes changeType, string fullPath)
    {
        ChangeType = changeType;
        FullPath = fullPath;
    }
}