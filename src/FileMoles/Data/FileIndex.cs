namespace FileMoles.Data;

public class FileIndex
{
    public string FullPath { get; set; }
    public long Length { get; init; }
    public DateTime CreationTime { get; init; }
    public DateTime LastWriteTime { get; init; }
    public DateTime LastAccessTime { get; init; }
    public FileAttributes Attributes { get; init; }

    public string Name => Path.GetFileName(FullPath);
    public string Directory => Path.GetDirectoryName(FullPath)!;

    public FileIndex(string fullPath)
    {
        FullPath = fullPath;
    }

    public FileInfo GetFileInfo() => new(FullPath);
}