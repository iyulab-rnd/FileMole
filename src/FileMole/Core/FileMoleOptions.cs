namespace FileMole.Core;

public class FileMoleOptions
{
    public List<Mole> Moles { get; set; } = [];
    public string? DatabasePath { get; set; }
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