using System.Security;
using System.IO;

namespace FileMoles.Storage;

public class LocalStorageProvider : IStorageProvider
{
    public Task<FileInfo> GetFileAsync(string filePath)
    {
        return Task.Run(() => new FileInfo(filePath));
    }

    public async Task<IEnumerable<FileInfo>> GetFilesAsync(string path)
    {
        return await Task.Run(() =>
        {
            var files = new List<FileInfo>();
            var directoryInfo = new DirectoryInfo(path);

            try
            {
                files.AddRange(directoryInfo.EnumerateFiles());
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied to directory: {path}. Error: {ex.Message}");
            }
            catch (SecurityException ex)
            {
                Console.WriteLine($"Security error accessing directory {path}. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing directory {path}. Error: {ex.Message}");
            }

            return files;
        });
    }

    public async Task<IEnumerable<DirectoryInfo>> GetDirectoriesAsync(string path)
    {
        return await Task.Run(() =>
        {
            var directories = new List<DirectoryInfo>();
            var directoryInfo = new DirectoryInfo(path);

            try
            {
                directories.AddRange(directoryInfo.EnumerateDirectories());
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied to directory: {path}. Error: {ex.Message}");
            }
            catch (SecurityException ex)
            {
                Console.WriteLine($"Security error accessing directory {path}. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing directory {path}. Error: {ex.Message}");
            }

            return directories;
        });
    }

    public static async Task<bool> HasAccessAsync(string path)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // 디렉토리에 대한 접근을 확인하기 위해 첫 번째 항목만 열거
                    using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                    return await Task.FromResult(enumerator.MoveNext());
                }
                else if (File.Exists(path))
                {
                    // 파일에 대한 접근을 비동기적으로 확인
                    using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
                    await fileStream.ReadAsync((new byte[1]).AsMemory(0, 1));
                }
                else
                {
                    return false;
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (SecurityException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    public async Task<Stream> OpenFileAsync(string path, FileMode mode)
    {
        return await Task.Run(() => new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None, 4096, true));
    }

    public async Task CreateDirectoryAsync(string path)
    {
        await Task.Run(() => Directory.CreateDirectory(path));
    }

    public async Task DeleteFileAsync(string path)
    {
        await Task.Run(() =>
        {
            if (File.Exists(path))
                File.Delete(path);
            else
                throw new FileNotFoundException($"File not found: {path}");
        });
    }

    public async Task DeleteDirectoryAsync(string path, bool recursive)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive);
            else
                throw new DirectoryNotFoundException($"Directory not found: {path}");
        });
    }

    public async Task<bool> ExistsAsync(string path)
    {
        return await Task.Run(() => File.Exists(path) || Directory.Exists(path));
    }

    public async Task MoveAsync(string sourcePath, string destinationPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(sourcePath))
                File.Move(sourcePath, destinationPath);
            else if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, destinationPath);
            else
                throw new FileNotFoundException($"Source path not found: {sourcePath}");
        });
    }

    public async Task CopyAsync(string sourcePath, string destinationPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(sourcePath))
                File.Copy(sourcePath, destinationPath, true);
            else if (Directory.Exists(sourcePath))
                CopyDirectory(sourcePath, destinationPath);
            else
                throw new FileNotFoundException($"Source path not found: {sourcePath}");
        });
    }

    private void CopyDirectory(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(destinationPath))
            Directory.CreateDirectory(destinationPath);

        foreach (string file in Directory.GetFiles(sourcePath))
        {
            string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (string dir in Directory.GetDirectories(sourcePath))
        {
            string destDir = Path.Combine(destinationPath, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }

    public async Task RenameAsync(string fullPath, string newFileName)
    {
        await Task.Run(() =>
        {
            if (File.Exists(fullPath))
            {
                string directory = Path.GetDirectoryName(fullPath)!;
                string newPath = Path.Combine(directory, newFileName);
                File.Move(fullPath, newPath);
            }
            else if (Directory.Exists(fullPath))
            {
                string parentDirectory = Path.GetDirectoryName(fullPath)!;
                string newPath = Path.Combine(parentDirectory, newFileName);
                Directory.Move(fullPath, newPath);
            }
            else
            {
                throw new FileNotFoundException($"Path not found: {fullPath}");
            }
        });
    }

    public async Task DeleteAsync(string fullPath)
    {
        await Task.Run(() =>
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, true);
            }
            else
            {
                throw new FileNotFoundException($"Path not found: {fullPath}");
            }
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}