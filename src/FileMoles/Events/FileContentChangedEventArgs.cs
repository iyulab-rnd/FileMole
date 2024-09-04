using FileMoles.Events;
using FileMoles.Diff;

public class FileContentChangedEventArgs : FileMoleEventArgs
{
    public DiffResult Diff { get; }

    public FileContentChangedEventArgs(FileSystemEvent internalEvent, DiffResult diff)
        : base(internalEvent)
    {
        Diff = diff;
    }
}