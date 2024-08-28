namespace FileMole.Storage;

public class LocalStorageProvider : IStorageProvider
{
    public async Task<IEnumerable<FMFileInfo>> GetFilesAsync(string path)
    {
        return await Task.Run(() =>
        {
            var directoryInfo = new DirectoryInfo(path);
            return directoryInfo.GetFiles().Select(fi => new FMFileInfo(
                fi.Name,
                fi.FullName,
                fi.Length,
                fi.CreationTime,
                fi.LastWriteTime,
                fi.LastAccessTime,
                fi.Attributes
            ));
        });
    }

    public async Task<IEnumerable<FMDirectoryInfo>> GetDirectoriesAsync(string path)
    {
        return await Task.Run(() =>
        {
            var directoryInfo = new DirectoryInfo(path);
            return directoryInfo.GetDirectories().Select(di => new FMDirectoryInfo(
                di.Name,
                di.FullName,
                di.CreationTime,
                di.LastWriteTime,
                di.LastAccessTime,
                di.Attributes
            ));
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
}