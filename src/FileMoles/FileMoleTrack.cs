using System.Collections.Concurrent;

namespace FileMoles;

public class FileMoleTrack
{
    private readonly ConcurrentDictionary<string, byte> _trackedFiles = new();

    public FileMoleTrack(string path)
    {
        Path = path;
    }

    public bool HasTrackedFiles => !_trackedFiles.IsEmpty;

    public string Path { get; }

    public void AddTrackedFile(string filePath)
    {
        _trackedFiles.TryAdd(filePath, 0);
    }

    public void RemoveTrackedFile(string filePath)
    {
        _trackedFiles.TryRemove(filePath, out _);
    }

    public bool IsTracked(string filePath)
    {
        return _trackedFiles.ContainsKey(filePath);
    }

    public IReadOnlyCollection<string> GetTrackedFiles()
    {
        return _trackedFiles.Keys.ToArray();
    }

    public void ClearTrackedFiles()
    {
        _trackedFiles.Clear();
    }
}