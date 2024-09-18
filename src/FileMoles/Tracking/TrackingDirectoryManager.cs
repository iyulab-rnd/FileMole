using FileMoles.Data;
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

    public TrackingDirectoryManager(string directoryPath, DbContext unitOfWork)
    {
        DirectoryPath = Path.GetFullPath(directoryPath);  // 경로 정규화
        _hillFolderPath = Path.Combine(DirectoryPath, ".hill");
        _backupFolderPath = Path.Combine(_hillFolderPath, "backups");
        _dbContext = unitOfWork;
        _ignoreManager = new TrackingIgnoreManager(Path.Combine(_hillFolderPath, ".filemoleignore"));
    }

    public async Task InitializeAsync()
    {
        if (_ignoreManager.IsIgnored(DirectoryPath))
        {
            return; // 무시된 디렉토리면 초기화하지 않음
        }

        await _dbContext.TrackingDirs.UpsertAsync(TrackingDir.CreateNew(DirectoryPath));
        CreateHillFolder();

        await CleanupAsync();
    }

    internal async Task UntrackingAsync()
    {
        await _dbContext.TrackingDirs.DeleteByPathAsync(DirectoryPath);
        await DeleteHillFolderAsync();
    }

    private async Task CleanupAsync()
    {
        if (Directory.Exists(_backupFolderPath))
        {
            var files = Directory.GetFiles(_backupFolderPath);
            if (files.Length == 0)
            {
                // 백업 폴더에 파일이 없으면 폴더를 삭제
                Directory.Delete(_backupFolderPath);
            }
        }

        if (Directory.Exists(_hillFolderPath))
        {
            var files = Directory.GetFiles(_hillFolderPath);
            var directories = Directory.GetDirectories(_hillFolderPath);

            // .hill 폴더 안에 백업 폴더와 다른 파일, 폴더가 없는 경우 삭제
            if (files.Length == 0 && directories.Length == 0)
            {
                Directory.Delete(_hillFolderPath);
            }
        }

        await Task.CompletedTask;
    }

    private void CreateHillFolder()
    {
        if (!Directory.Exists(_hillFolderPath))
        {
            Directory.CreateDirectory(_hillFolderPath);
        }

        if (!Directory.Exists(_backupFolderPath))
        {
            Directory.CreateDirectory(_backupFolderPath);
        }
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

    public void BackupFile(string filePath)
    {
        var backupPath = GetBackupFilePath(filePath);
        var backupDir = Path.GetDirectoryName(backupPath)!;

        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        File.Copy(filePath, backupPath, true); // 파일 덮어쓰기
    }

    public bool HasBackup(string filePath)
    {
        var backupPath = GetBackupFilePath(filePath);
        return File.Exists(backupPath);
    }
}

internal partial class TrackingDirectoryManager
{
    public static bool HasHill(string directoryPath)
    {
        var hillFolderPath = Path.Combine(Path.GetFullPath(directoryPath), ".hill");
        return Directory.Exists(hillFolderPath);
    }
}
