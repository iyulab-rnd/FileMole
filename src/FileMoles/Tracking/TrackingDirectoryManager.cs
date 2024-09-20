using FileMoles.Data;
using FileMoles.Internal;
using MimeKit.Cryptography;
using System.Security.Cryptography;
using System.Text;

namespace FileMoles.Tracking;

internal partial class TrackingDirectoryManager
{
    private readonly DbContext _dbContext;
    public string DirectoryPath { get; }
    private readonly string _hillFolderPath;
    private readonly string _backupFolderPath;
    private readonly TrackingIgnoreManager _ignoreManager;

    private TrackingDirectoryManager(string fullPath, DbContext unitOfWork)
    {
        DirectoryPath = IOHelper.NormalizePath(fullPath);  // 경로 정규화
        _hillFolderPath = Path.Combine(DirectoryPath, FileMoleGlobalOptions.HillName);
        _backupFolderPath = Path.Combine(_hillFolderPath, "backups");
        _dbContext = unitOfWork;
        _ignoreManager = TrackingIgnoreManager.CreateNew(Path.Combine(DirectoryPath, FileMoleGlobalOptions.IgnoreFileName));
    }

    private async Task InitializeAsync()
    {
        await _dbContext.TrackingDirs.UpsertAsync(TrackingDir.CreateNew(DirectoryPath));
        CreateHillFolder();
    }

    private void CreateHillFolder()
    {
        IOHelper.CreateDirectory(_hillFolderPath);
        IOHelper.CreateDirectory(_backupFolderPath);
    }

    private async Task ScanTrakingFilesAsync()
    {
        var files = Directory.GetFiles(DirectoryPath, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            if (ShouldTrackingFile(file))
            {
                await TrackingFileAsync(file);
            }
        }
    }

    internal async Task UntrackingAsync()
    {
        await _dbContext.TrackingDirs.DeleteByPathAsync(DirectoryPath);
        await DeleteHillFolderAsync();
    }
    
    private async Task DeleteHillFolderAsync()
    {
        if (Directory.Exists(_hillFolderPath))
        {
            Directory.Delete(_hillFolderPath, true);
        }

        await Task.CompletedTask;
    }

    public bool IsIgnored(string filePath)
    {
        return _ignoreManager.IsIgnored(filePath);
    }

    private static string GetRelativePathHash(string relativePath)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(relativePath));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    public string GetBackupFilePath(string filePath)
    {
        var relativePath = Path.GetRelativePath(DirectoryPath, filePath);
        var relativePathHash = GetRelativePathHash(relativePath);
        return Path.Combine(_backupFolderPath, $"{relativePathHash}.bak");
    }

    private async Task BackupFileAsync(string filePath)
    {
        var backupPath = GetBackupFilePath(filePath);
        var backupDir = Path.GetDirectoryName(backupPath)!;

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        await RetryFile.CopyAsync(filePath, backupPath);
    }

    public bool HasBackup(string filePath)
    {
        var backupPath = GetBackupFilePath(filePath);
        return File.Exists(backupPath);
    }

    internal bool ShouldTrackingFile(string filePath)
    {
        var relativePath = Path.GetRelativePath(DirectoryPath, filePath);
        return !_ignoreManager.IsIgnored(relativePath);
    }

    internal bool IsTrackingFile(string filePath) => ShouldTrackingFile(filePath);

    internal async Task TrackingFileAsync(string filePath)
    {
        if (IsIgnored(filePath))
        {
            await _ignoreManager.IncludeFilePathAsync(filePath);
        }

        await BackupFileAsync(filePath);
    }

    internal async Task UntrackingFileAsync(string filePath)
    {
        await _ignoreManager.ExcludeFilePathAsync(filePath);

        var backupPath = GetBackupFilePath(filePath);
        if (File.Exists(backupPath))
        {
            await RetryFile.DeleteAsync(backupPath);
        }

        await Task.CompletedTask;
    }
}

internal partial class TrackingDirectoryManager
{
    public static async Task<TrackingDirectoryManager> CreateByDirectoryAsync(string dirPath, DbContext unitOfWork)
    {
        if (Directory.Exists(dirPath))
        {
            var manager = new TrackingDirectoryManager(dirPath, unitOfWork);
            await manager.InitializeAsync();
            return manager;
        }
        else
            throw new FileNotFoundException(dirPath);
    }

    public static async Task<TrackingDirectoryManager> CreateByFileAsync(string filePath, DbContext unitOfWork)
    {
        if (File.Exists(filePath))
        {
            var directory = Path.GetDirectoryName(filePath)!;
            var manager = new TrackingDirectoryManager(directory, unitOfWork);
            await manager.InitializeAsync();
            await manager.TrackingFileAsync(filePath);
            return manager;
        }
        else
            throw new FileNotFoundException(filePath);
    }

    public static bool HasHill(string directoryPath)
    {
        var hillFolderPath = Path.Combine(directoryPath, FileMoleGlobalOptions.HillName);
        return Directory.Exists(hillFolderPath);
    }
}
