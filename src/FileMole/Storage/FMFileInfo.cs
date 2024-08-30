using System.Reflection.PortableExecutable;

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

    private FMFileInfo(string name, string fullPath, long size, DateTime creationTime, DateTime lastWriteTime, DateTime lastAccessTime, FileAttributes attributes, string? fileHash = null, string? lastFileHash = null)
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

    private static FMFileInfo CreateInaccessible(string name, string fullPath)
    {
        return new FMFileInfo(
            name,
            fullPath,
            -1,
            DateTime.MinValue,
            DateTime.MinValue,
            DateTime.MinValue,
            FileAttributes.ReadOnly | FileAttributes.System
        );
    }

    public static FMFileInfo FromFileInfo(FileInfo fileInfo)
    {
        try
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
        catch (UnauthorizedAccessException)
        {
            return CreateInaccessible(fileInfo.Name, fileInfo.FullName);
        }
    }

    public static FMFileInfo FromPath(string file)
    {
        var fileInfo = new FileInfo(file);
        return FromFileInfo(fileInfo);
    }

    // Computed properties
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
    public bool IsHidden => (Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    public bool IsSystem => (Attributes & FileAttributes.System) == FileAttributes.System;

    public void UpdateFileHash(string newHash)
    {
        FileHash = newHash;
    }

    public override string ToString()
    {
        return $"File: {Name}, Size: {Size} bytes, Last Modified: {LastWriteTime}";
    }

    internal static FMFileInfo CreateNew(string name, string fullPath, long size, DateTime creationTime, DateTime lastWriteTime, DateTime lastAccessTime, FileAttributes attributes, string? fileHash = null, string? lastFileHash = null)
    {
        return new FMFileInfo(name, fullPath, size, creationTime, lastWriteTime, lastAccessTime, attributes, fileHash, lastFileHash);
    }
}