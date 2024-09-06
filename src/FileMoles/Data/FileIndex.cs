namespace FileMoles.Data;

public class FileIndex
{
    public string FullPath { get; set; }
    public long Size { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    public FileAttributes Attributes { get; init; }

    public string Name => Path.GetFileName(FullPath);
    public string Directory => Path.GetDirectoryName(FullPath)!;

    public FileIndex(string fullPath)
    {
        FullPath = fullPath;
    }

    public FileInfo GetFileInfo() => new(FullPath);
}