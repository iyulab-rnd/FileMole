using FileMoles.Events;
using FileMoles.Diff;

public class FileContentChangedEventArgs : FileMoleEventArgs
{
    public DiffResult Diff { get; }

    public FileContentChangedEventArgs(
        string fullPath,
        string? oldFullPath,
        WatcherChangeTypes changeType,
        bool isDirectory,
        DiffResult diff)
        : base(fullPath, oldFullPath, changeType, isDirectory)
    {
        Diff = diff;
    }
}