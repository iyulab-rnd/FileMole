namespace FileMoles.Data;

internal class FileIndex
{
    public required string Directory { get; set; }
    public required string Name { get; set; }
    public long Size { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public FileAttributes Attributes { get; set; }
    public DateTime LastScanned { get; set; }

    internal static FileIndex CreateNew(FileInfo file)
    {
        return new FileIndex()
        {
            Directory = file.Directory!.FullName,
            Name = file.Name,
            Size = file.Length,
            Created = file.CreationTimeUtc,
            Modified = file.LastWriteTimeUtc,
            Attributes = file.Attributes,
            LastScanned = DateTime.UtcNow
        };
    }

    internal bool IsChanged(FileInfo file)
    {
        const double SizeThreshold = 1.0; // 바이트 단위
        const double TimeThreshold = 1.0; // 초 단위

        return Math.Abs(this.Size - file.Length) > SizeThreshold ||
              Math.Abs((this.Created - file.CreationTimeUtc).TotalSeconds) > TimeThreshold ||
              Math.Abs((this.Modified - file.LastWriteTimeUtc).TotalSeconds) > TimeThreshold ||
              this.Attributes != file.Attributes;
    }
}