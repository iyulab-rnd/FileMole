namespace FileMoles.Data;

internal class TrackingFile
{
    public required string FullPath { get; set; }

    internal static TrackingFile CreateNew(string fullPath)
    {
        return new TrackingFile
        {
            FullPath = fullPath
        };
    }
}