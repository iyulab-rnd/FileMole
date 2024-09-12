namespace FileMoles.Events;

public class FileMoleEventArgs : EventArgs
{
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public WatcherChangeTypes ChangeType { get; }
    public bool IsDirectory { get; }

    public FileMoleEventArgs(
        string fullPath,
        string? oldFullPath,
        WatcherChangeTypes changeType,
        bool isDirectory)
    {
        FullPath = fullPath;
        OldFullPath = oldFullPath;
        ChangeType = changeType;
        IsDirectory = isDirectory;
    }
}
