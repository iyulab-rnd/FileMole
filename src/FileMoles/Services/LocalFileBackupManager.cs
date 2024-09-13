using FileMoles.Interfaces;
using FileMoles.Internal;

namespace FileMoles.Services;

internal class LocalFileBackupManager : IFileBackupManager
{
    private readonly FileHashGenerator _hashGenerator;
    
    public LocalFileBackupManager()
    {
        _hashGenerator = new FileHashGenerator();
    }

    public Task<bool> BackupExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var backupPath = GetBackupPath(filePath);
        return Task.FromResult(File.Exists(backupPath));
    }

    public async Task BackupFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var backupPath = GetBackupPath(filePath);
        IOHelper.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        await SafeFileIO.CopyAsync(filePath, backupPath, cancellationToken: cancellationToken);
    }

    public Task<string> GetBackupPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetBackupPath(filePath));
    }

    public async Task DeleteBackupAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var backupPath = GetBackupPath(filePath);
        if (File.Exists(backupPath))
        {
            await SafeFileIO.DeleteAsync(backupPath, cancellationToken: cancellationToken);
        }
    }

    public async Task<bool> HasFileChangedAsync(string filePath, CancellationToken cancellationToken = default)
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
            var currentHash = await _hashGenerator.GenerateHashAsync(filePath, cancellationToken);
            var backupHash = await _hashGenerator.GenerateHashAsync(backupPath, cancellationToken);
            return currentHash != backupHash;
        }

        return false;
    }

    private string GetBackupPath(string filePath)
    {
        var targetDirectory = Path.GetDirectoryName(filePath)!;
        var targetFileName = Path.GetFileName(filePath);
        return Path.Combine(targetDirectory, FileMoleGlobalOptions.MolePathName, "backups", targetFileName);
    }
}