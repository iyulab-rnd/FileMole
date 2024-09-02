using FileMoles.Events;

namespace FileMoles;

public class FileMoleEventArgs : EventArgs
{
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public WatcherChangeTypes ChangeType { get; }
    public bool IsDirectory { get; }

    public FileMoleEventArgs(FileSystemEvent internalEvent)
    {
        FullPath = internalEvent.FullPath;
        OldFullPath = internalEvent.OldFullPath;
        ChangeType = internalEvent.ChangeType;
        IsDirectory = internalEvent.IsDirectory;
    }
}
