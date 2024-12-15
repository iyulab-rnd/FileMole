namespace FileMoles.Core.Interfaces;

public class FileSystemChangeEventArgs : EventArgs
{
    public string ProviderId { get; }
    public IEnumerable<FileSystemChange> Changes { get; }

    public FileSystemChangeEventArgs(string providerId, IEnumerable<FileSystemChange> changes)
    {
        ProviderId = providerId;
        Changes = changes;
    }
}