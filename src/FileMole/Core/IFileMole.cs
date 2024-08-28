using FileMole.Storage;
using FileMole.Events;
using FileMole.Indexing;

namespace FileMole.Core;

public interface IFileMole : IDisposable
{
    event EventHandler<FileSystemEvent> FileSystemChanged;
    Task WatchDirectoryAsync(string path);
    Task IndexAllAsync();
    Task<IEnumerable<FMFileInfo>> SearchAsync(string searchTerm);
}