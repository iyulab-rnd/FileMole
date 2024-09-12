namespace FileMoles.Data;

internal class FileIndex
{
    public int Id { get; set; }
    public required string FullPath { get; set; }
    public required string Name { get; set; }
    public long Size { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public FileAttributes Attributes { get; set; }

    internal static FileIndex CreateNew(FileInfo file)
    {
        return new FileIndex()
        {
            FullPath = file.FullName,
            Name = file.Name,
            Size = file.Length,
            Created = file.CreationTime,
            Modified = file.LastWriteTime,
            Attributes = file.Attributes
        };
    }
}