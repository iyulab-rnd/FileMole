namespace FileMoles.Data;

internal class TrackingDir
{
    public required string Path { get; set; }

    internal static TrackingDir CreateNew(string path)
    {
        return new TrackingDir
        {
            Path = path
        };
    }
}