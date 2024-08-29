namespace FileMole.Storage;

public class FMFileInfo
{
    public string Name { get; }
    public string FullPath { get; }
    public long Size { get; }
    public DateTime CreationTime { get; }
    public DateTime LastWriteTime { get; }
    public DateTime LastAccessTime { get; }
    public FileAttributes Attributes { get; }
    public string? FileHash { get; set; }
    public string? LastFileHash { get; }

    public FMFileInfo(string name, string fullPath, long size, DateTime creationTime, DateTime lastWriteTime, DateTime lastAccessTime, FileAttributes attributes, string? fileHash = null, string? lastFileHash = null)
    {
        Name = name;
        FullPath = fullPath;
        Size = size;
        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
        LastAccessTime = lastAccessTime;
        Attributes = attributes;
        FileHash = fileHash;
        LastFileHash = lastFileHash;
    }

    // Computed properties
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
    public bool IsHidden => (Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    public bool IsSystem => (Attributes & FileAttributes.System) == FileAttributes.System;

    // Factory method to create FMFileInfo from System.IO.FileInfo
    public static FMFileInfo FromFileInfo(FileInfo fileInfo)
    {
        return new FMFileInfo(
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length,
            fileInfo.CreationTime,
            fileInfo.LastWriteTime,
            fileInfo.LastAccessTime,
            fileInfo.Attributes
        );
    }

    // Method to update file hash
    public void UpdateFileHash(string newHash)
    {
        FileHash = newHash;
    }

    public override string ToString()
    {
        return $"File: {Name}, Size: {Size} bytes, Last Modified: {LastWriteTime}";
    }
}