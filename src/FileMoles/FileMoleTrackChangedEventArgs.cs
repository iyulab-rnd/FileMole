using FileMoles.Events;
using FileMoles;
using FileMoles.Diff;

public class FileMoleTrackChangedEventArgs : FileMoleEventArgs
{
    public DiffResult Diff { get; }

    public FileMoleTrackChangedEventArgs(FileSystemEvent internalEvent, DiffResult diff)
        : base(internalEvent)
    {
        Diff = diff;
    }
}