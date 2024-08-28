namespace FileMole.Events;

public interface IFMFileSystemWatcher : IDisposable
{
    event EventHandler<FileSystemEvent> FileSystemChanged;
    Task WatchDirectoryAsync(string path);
}