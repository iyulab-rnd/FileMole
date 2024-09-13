namespace FileMoles.Interfaces;

public interface IFileBackupManager
{
    Task<bool> BackupExistsAsync(string filePath, CancellationToken cancellationToken = default);
    Task BackupFileAsync(string filePath, CancellationToken cancellationToken = default);
    Task<string> GetBackupPathAsync(string filePath, CancellationToken cancellationToken = default);
    Task DeleteBackupAsync(string filePath, CancellationToken cancellationToken = default);
    Task<bool> HasFileChangedAsync(string filePath, CancellationToken cancellationToken = default);
}