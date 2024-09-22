using FileMoles.Diff;
using System.Security.Cryptography;
using System.Text;
using FileMoles.Internal;

namespace FileMoles.Tracking;

public static class HillUtils
{
    private static readonly string hillName = FileMoleGlobalOptions.HillName; // .hill
    private static readonly string backupName = "backups"; // .hill/backups
    private static readonly TimeSpan allowedTimeDifference = TimeSpan.FromSeconds(1); // Allow 1 second difference

    public static async Task BackupAsync(string filePath)
    {
        string backupFilePath = await GetBackupFilePath(filePath);
        await CopyFileWithAttributesAsync(filePath, backupFilePath);
    }

    public static async Task DeleteBackupAsync(string filePath)
    {
        string backupFilePath = await GetBackupFilePath(filePath);
        if (File.Exists(backupFilePath))
        {
            File.Delete(backupFilePath);
        }
    }

    public static async Task<DiffResult?> GetDiffAsync(string filePath)
    {
        string backupFilePath = await GetBackupFilePath(filePath);
        if (!File.Exists(backupFilePath))
        {
            return null;
        }

        IDiffStrategy diffStrategy = DiffStrategyFactory.CreateStrategy(filePath);
        return await diffStrategy.GenerateDiffAsync(backupFilePath, filePath);
    }

    public static async Task<bool> IsChangedAsync(string filePath)
    {
        string backupFilePath = await GetBackupFilePath(filePath);
        if (!File.Exists(backupFilePath))
        {
            return true; // 백업 파일이 없으면 변경된 것으로 간주
        }

        // 1. 마지막 수정 시간 비교
        if (!CompareLastWriteTime(filePath, backupFilePath))
        {
            return true;
        }

        // 2. 파일 크기 비교
        if (!CompareFileSize(filePath, backupFilePath))
        {
            return true;
        }

        // 3. 부분 해시 비교
        if (!await ComparePartialHashAsync(filePath, backupFilePath))
        {
            return true;
        }

        // 4. 전체 파일 내용 비교 (변경되지 않았을 가능성이 높은 경우에만 실행)
        return !await CompareFileContentsAsync(filePath, backupFilePath);
    }

    private static async Task<string> GetBackupFilePath(string filePath)
    {
        string hillFolder = FindOrCreateHillFolder(filePath);
        string backupFolder = GetBackupFolder(hillFolder);

        string relativeFilePath = Path.GetRelativePath(hillFolder, filePath);
        string fileHash = await CalculateRelativePathHashAsync(relativeFilePath);
        var backupFileName = $"{fileHash}.bak";
        string backupFilePath = Path.Combine(backupFolder, backupFileName);
        return backupFilePath;
    }

    private static string FindOrCreateHillFolder(string path)
    {
        string originalPath = path;
        while (!string.IsNullOrEmpty(path))
        {
            string hillPath = Path.Combine(path, hillName);
            if (Directory.Exists(hillPath))
            {
                return hillPath;
            }
            string parentPath = Path.GetDirectoryName(path)!;
            if (parentPath == path) // We've reached the root
            {
                break;
            }
            path = parentPath;
        }

        // If we're here, we didn't find a .hill folder, so create one in the original path
        string newHillPath = Path.Combine(Path.GetDirectoryName(originalPath) ?? "", hillName);
        IOHelper.CreateDirectory(newHillPath);
        return newHillPath;
    }

    private static string GetBackupFolder(string hillFolder)
    {
        string backupFolder = Path.Combine(hillFolder, backupName);
        Directory.CreateDirectory(backupFolder);
        return backupFolder;
    }

    private static async Task<string> CalculateRelativePathHashAsync(string relativePath)
    {
        using var md5 = MD5.Create();
        byte[] inputBytes = Encoding.UTF8.GetBytes(relativePath);
        byte[] hashBytes = await md5.ComputeHashAsync(new MemoryStream(inputBytes));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static async Task CopyFileWithAttributesAsync(string sourceFilePath, string destinationFilePath)
    {
        // Copy file content
        using (FileStream sourceStream = new(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
        {
            using FileStream destinationStream = new(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await sourceStream.CopyToAsync(destinationStream);
        }

        // Copy file attributes
        File.SetAttributes(destinationFilePath, File.GetAttributes(sourceFilePath));

        // Copy timestamps
        File.SetCreationTimeUtc(destinationFilePath, File.GetCreationTimeUtc(sourceFilePath));
        File.SetLastAccessTimeUtc(destinationFilePath, File.GetLastAccessTimeUtc(sourceFilePath));
        File.SetLastWriteTimeUtc(destinationFilePath, File.GetLastWriteTimeUtc(sourceFilePath));
    }

    private static bool CompareLastWriteTime(string file1, string file2)
    {
        DateTime time1 = File.GetLastWriteTimeUtc(file1);
        DateTime time2 = File.GetLastWriteTimeUtc(file2);
        return Math.Abs((time1 - time2).TotalSeconds) <= allowedTimeDifference.TotalSeconds;
    }

    private static bool CompareFileSize(string file1, string file2)
    {
        return new FileInfo(file1).Length == new FileInfo(file2).Length;
    }

    private static async Task<bool> ComparePartialHashAsync(string file1, string file2)
    {
        const int bufferSize = 4096;
        using var md5 = MD5.Create();
        using var stream1 = new FileStream(file1, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var stream2 = new FileStream(file2, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        // 파일의 처음, 중간, 끝 부분의 해시를 비교
        return await ComputePartialHashAsync(stream1, md5) == await ComputePartialHashAsync(stream2, md5);
    }

    private static async Task<string> ComputePartialHashAsync(FileStream stream, MD5 md5)
    {
        byte[] buffer = new byte[4096];
        int bytesRead;

        // 처음 부분
        bytesRead = await stream.ReadAsync(buffer);
        md5.TransformBlock(buffer, 0, bytesRead, null, 0);

        // 중간 부분
        if (stream.Length > 8192)
        {
            stream.Seek(stream.Length / 2, SeekOrigin.Begin);
            bytesRead = await stream.ReadAsync(buffer);
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        // 끝 부분
        if (stream.Length > 4096)
        {
            stream.Seek(-4096, SeekOrigin.End);
            bytesRead = await stream.ReadAsync(buffer);
            md5.TransformFinalBlock(buffer, 0, bytesRead);
        }
        else
        {
            md5.TransformFinalBlock([], 0, 0);
        }

        return BitConverter.ToString(md5.Hash!).Replace("-", "").ToLowerInvariant();
    }

    private static async Task<bool> CompareFileContentsAsync(string file1, string file2)
    {
        const int bufferSize = 4096;
        using var stream1 = new FileStream(file1, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var stream2 = new FileStream(file2, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            byte[] buffer1 = new byte[bufferSize];
            byte[] buffer2 = new byte[bufferSize];
            int count1 = await stream1.ReadAsync(buffer1.AsMemory(0, bufferSize));
            int count2 = await stream2.ReadAsync(buffer2.AsMemory(0, bufferSize));

            if (count1 != count2)
            {
                return false;
            }

            if (count1 == 0)
            {
                return true;
            }

            for (int i = 0; i < count1; i++)
            {
                if (buffer1[i] != buffer2[i])
                {
                    return false;
                }
            }
        }
    }
}