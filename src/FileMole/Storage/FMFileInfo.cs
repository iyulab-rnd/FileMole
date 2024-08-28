namespace FileMole.Storage;

public class FMFileInfo
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public long Size { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public FileAttributes Attributes { get; set; }

    public FMFileInfo(string name, string fullPath, long size, DateTime creationTime, DateTime lastWriteTime, DateTime lastAccessTime, FileAttributes attributes)
    {
        Name = name;
        FullPath = fullPath;
        Size = size;
        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
        LastAccessTime = lastAccessTime;
        Attributes = attributes;
    }

    // 필요에 따라 추가 메서드를 구현할 수 있습니다.
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
    public bool IsHidden => (Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    public bool IsSystem => (Attributes & FileAttributes.System) == FileAttributes.System;

    public override string ToString()
    {
        return $"File: {Name}, Size: {Size} bytes, Last Modified: {LastWriteTime}";
    }
}