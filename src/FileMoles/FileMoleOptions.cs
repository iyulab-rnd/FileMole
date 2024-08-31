namespace FileMoles;

public class FileMoleOptions
{
    public List<Mole> Moles { get; set; } = [];
    public string? DatabasePath { get; set; }
    public int DebounceTime { get; set; } = 60000; // 1 분
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