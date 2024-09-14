namespace FileMoles.Data;

internal class TrackingFile
{
    public required string FullPath { get; set; }
    public required string Hash { get; set; }
    public bool IsDirectory { get; set; }
    public DateTime LastTrackedTime { get; set; }

    public string GetBackupFileName() => $"{Hash}.bak";

    public static string GeneratePathHash(string fullPath)
    {
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(fullPath);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(pathBytes);
        return Convert.ToBase64String(hashBytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
    }
}