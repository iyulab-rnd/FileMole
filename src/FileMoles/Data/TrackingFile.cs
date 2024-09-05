namespace FileMoles.Data;

public class TrackingFile
{
    public int Id { get; set; }
    public string FullPath { get; set; }
    public string BackupFileName { get; set; }
    public bool IsDirectory { get; set; }
    public DateTime LastTrackedTime { get; set; }

    public TrackingFile(string fullPath, bool isDirectory)
    {
        FullPath = fullPath;
        IsDirectory = isDirectory;
        LastTrackedTime = DateTime.UtcNow;
        BackupFileName = GenerateBackupFileName(fullPath);
    }

    private static string GenerateBackupFileName(string fullPath)
    {
        // 파일 경로의 해시를 생성하여 고유한 백업 파일명 생성
        var pathBytes = System.Text.Encoding.UTF8.GetBytes(fullPath);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(pathBytes);
        return Convert.ToBase64String(hashBytes).Replace("/", "_").Replace("+", "-").TrimEnd('=') + ".bak";
    }
}