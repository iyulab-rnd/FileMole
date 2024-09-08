using FileMoles.Utils;

namespace FileMoles.Services;

public class LocalBackupManager : IBackupManager
{
    private readonly HashGenerator _hashGenerator;

    public LocalBackupManager()
    {
        _hashGenerator = new HashGenerator();
    }

    public Task<bool> BackupExistsAsync(string filePath)
    {
        var backupPath = GetBackupPath(filePath);
        return Task.FromResult(File.Exists(backupPath));
    }

    public async Task BackupFileAsync(string filePath)
    {
        var backupPath = GetBackupPath(filePath);
        IOHelper.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await FileSafe.CopyWithRetryAsync(filePath, backupPath);
    }

    public Task<string> GetBackupPathAsync(string filePath)
    {
        return Task.FromResult(GetBackupPath(filePath));
    }

    public async Task DeleteBackupAsync(string filePath)
    {
        var backupPath = GetBackupPath(filePath);
        if (File.Exists(backupPath))
        {
            await FileSafe.DeleteRetryAsync(backupPath);
        }
    }

    public async Task<bool> HasFileChangedAsync(string filePath)
    {
        var backupPath = GetBackupPath(filePath);
        if (!File.Exists(backupPath))
        {
            return true;
        }

        var fileInfo = new FileInfo(filePath);
        var backupInfo = new FileInfo(backupPath);
        if (fileInfo.Length != backupInfo.Length || fileInfo.LastWriteTimeUtc != backupInfo.LastWriteTimeUtc)
        {
            var currentHash = await _hashGenerator.GenerateHashAsync(filePath);
            var backupHash = await _hashGenerator.GenerateHashAsync(backupPath);
            return currentHash != backupHash;
        }

        return false;
    }

    private static string GetBackupPath(string filePath)
    {
        var targetDirectory = Path.GetDirectoryName(filePath)!;
        var targetFileName = Path.GetFileName(filePath);
        return Path.Combine(targetDirectory, FileMoleGlobalOptions.MolePathName, ".backups", targetFileName);
    }
}