namespace FileMoles.Services;

public interface IBackupManager
{
    Task<bool> BackupExistsAsync(string filePath);
    Task BackupFileAsync(string filePath);
    Task<string> GetBackupPathAsync(string filePath);
    Task DeleteBackupAsync(string filePath);
    Task<bool> HasFileChangedAsync(string filePath);
}