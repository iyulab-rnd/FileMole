using FileMoles.Diff;
using System.IO;
namespace FileMoles.Events;

/// <summary>
/// 라이브러리 내부에서만 사용되는 이벤트 클래스
/// </summary>
internal class FileSystemEvent
{
    public WatcherChangeTypes ChangeType { get; }
    public string FullPath { get; }
    public string? OldFullPath { get; }
    public bool IsDirectory { get; }

    public FileSystemEvent(WatcherChangeTypes changeType, string fullPath, string? oldFullPath = null)
    {
        ChangeType = changeType;
        FullPath = fullPath;
        OldFullPath = oldFullPath;
        IsDirectory = Directory.Exists(fullPath);
    }

    internal FileMoleEventArgs CreateFileMoleEventArgs()
    {
        return new FileMoleEventArgs(
            FullPath,
            OldFullPath,
            ChangeType,
            IsDirectory);
    }

    internal FileContentChangedEventArgs CreateFileContentChangedEventArgs(DiffResult diff)
    {
        return new FileContentChangedEventArgs(
            FullPath,
            OldFullPath,
            ChangeType,
            IsDirectory,
            diff);
    }
}