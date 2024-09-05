namespace FileMoles;

public class TrackingDirectory
{
    private readonly HashSet<string> _trackedFiles = [];

    public TrackingDirectory(string path)
    {
        Path = path;
    }

    public bool HasTrackedFiles => _trackedFiles.Count > 0;

    public string Path { get; }

    public void AddTrackedFile(string filePath)
    {
        _trackedFiles.Add(filePath);
    }

    public void RemoveTrackedFile(string filePath)
    {
        _trackedFiles.Remove(filePath);
    }

    public bool IsTracked(string filePath)
    {
        return _trackedFiles.Contains(filePath);
    }

    public IReadOnlyCollection<string> GetTrackedFiles()
    {
        return _trackedFiles.ToArray();
    }

    public void ClearTrackedFiles()
    {
        _trackedFiles.Clear();
    }
}