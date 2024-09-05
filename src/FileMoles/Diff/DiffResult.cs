namespace FileMoles.Diff;

public abstract class DiffResult
{
    public string? FileType { get; set; }
    public bool IsChanged { get; internal set; }
    public bool IsInitial { get; internal set; }
}