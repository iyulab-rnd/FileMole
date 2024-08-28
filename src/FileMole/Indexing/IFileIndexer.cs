using FileMole.Storage;

namespace FileMole.Indexing;

public interface IFileIndexer
{
    Task IndexFileAsync(FMFileInfo file);
    Task<IEnumerable<FMFileInfo>> SearchAsync(string searchTerm);
    Task<IDictionary<string, int>> GetFileCountByDriveAsync();
}