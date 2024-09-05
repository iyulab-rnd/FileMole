
namespace FileMoles;

public class FileMoleOptions
{
    public List<Mole> Moles { get; set; } = [];
    public string? DataPath { get; set; }
    public int DebounceTime { get; set; } = 60_000 * 1; // 1 minute
    public long MaxFileSizeBytes { get; set; } = 1024 * 1024 * 200; // 200 MB (1KB * 1MB * 200)

    internal string GetDataPath()
    {
        return DataPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileMole");
    }
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