namespace FileMoles;

public class FileMoleOptions
{
    public List<Mole> Moles { get; set; } = [];
    public string? DatabasePath { get; set; }
    public int DebounceTime { get; set; } = 60000; // 1 minute
    public long MaxFileSizeBytes { get; set; } = 200 * 1024 * 1024; // 200 MB
}

public class Mole
{
    public required string Path { get; set; }
    public MoleType Type { get; set; } = MoleType.Local;
    public string Provider { get; set; } = "Default";
}

public enum MoleType
{
    Local,
    Remote,
    Cloud
}