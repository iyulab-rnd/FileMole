namespace FileMoles.Core.Models;

public class FileSystemItem
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string Extension { get; set; }
    public FileAttributes Attributes { get; set; }
    public IDictionary<string, object> Metadata { get; set; }
    public FileSecurityInfo Security { get; set; }

    public FileSystemItem()
    {
        Metadata = new Dictionary<string, object>();
    }

    public bool IsHidden => (Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
    public bool IsSystem => (Attributes & FileAttributes.System) == FileAttributes.System;

    public string GetRelativePath(string basePath)
    {
        if (string.IsNullOrEmpty(basePath)) return FullPath;
        return FullPath.StartsWith(basePath)
            ? FullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar)
            : FullPath;
    }
}
