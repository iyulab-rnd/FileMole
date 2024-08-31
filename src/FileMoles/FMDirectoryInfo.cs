namespace FileMoles;

public class FMDirectoryInfo
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public FileAttributes Attributes { get; set; }

    public FMDirectoryInfo(string name, string fullPath, DateTime creationTime, DateTime lastWriteTime, DateTime lastAccessTime, FileAttributes attributes)
    {
        Name = name;
        FullPath = fullPath;
        CreationTime = creationTime;
        LastWriteTime = lastWriteTime;
        LastAccessTime = lastAccessTime;
        Attributes = attributes;
    }

    // 편의 속성들
    public bool IsHidden => (Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    public bool IsSystem => (Attributes & FileAttributes.System) == FileAttributes.System;

    // 부모 디렉토리 정보를 가져오는 메서드
    public FMDirectoryInfo? GetParent()
    {
        var parentPath = Path.GetDirectoryName(FullPath);
        if (parentPath == null)
            return null;

        var parentInfo = new DirectoryInfo(parentPath);
        return new FMDirectoryInfo(
            parentInfo.Name,
            parentInfo.FullName,
            parentInfo.CreationTime,
            parentInfo.LastWriteTime,
            parentInfo.LastAccessTime,
            parentInfo.Attributes
        );
    }

    // 하위 디렉토리 목록을 가져오는 메서드
    public IEnumerable<FMDirectoryInfo> GetDirectories()
    {
        var directories = new List<FMDirectoryInfo>();
        foreach (var dir in Directory.GetDirectories(FullPath))
        {
            var dirInfo = new DirectoryInfo(dir);
            directories.Add(new FMDirectoryInfo(
                dirInfo.Name,
                dirInfo.FullName,
                dirInfo.CreationTime,
                dirInfo.LastWriteTime,
                dirInfo.LastAccessTime,
                dirInfo.Attributes
            ));
        }
        return directories;
    }

    // 디렉토리 내의 파일 목록을 가져오는 메서드
    public IEnumerable<FMFileInfo> GetFiles()
    {
        var files = new List<FMFileInfo>();
        foreach (var file in Directory.GetFiles(FullPath))
        {
            var fmFileInfo = FMFileInfo.FromPath(file);
            files.Add(fmFileInfo);
        }
        return files;
    }

    public override string ToString()
    {
        return $"Directory: {Name}, Last Modified: {LastWriteTime}";
    }
}