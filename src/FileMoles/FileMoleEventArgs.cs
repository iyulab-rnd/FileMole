using FileMoles.Events;

namespace FileMoles;

public class FileMoleEventArgs : EventArgs
{
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public WatcherChangeTypes ChangeType { get; }
    public bool IsDirectory { get; }

    internal FileMoleEventArgs(FileSystemEvent internalEvent)
    {
        FullPath = internalEvent.FullPath;
        OldFullPath = internalEvent.OldFullPath;
        ChangeType = internalEvent.ChangeType;
        IsDirectory = internalEvent.IsDirectory;
    }
}
