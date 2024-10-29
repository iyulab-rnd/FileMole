using System.Runtime.InteropServices;

namespace FileMoles.Internal;

internal static class IOHelper
{
    internal static bool IsHidden(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.StartsWith('.'))
        {
            return true;
        }

        var pathParts = filePath.Split(Path.DirectorySeparatorChar);
        if (pathParts.Any(part => part.StartsWith('.')))
        {
            return true;
        }

        try
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                return (fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
            }
            else if (Directory.Exists(filePath))
            {
                var currentPath = filePath;
                while (!string.IsNullOrEmpty(currentPath))
                {
                    var dirInfo = new DirectoryInfo(currentPath);

                    // 루트 디렉토리인 경우 Hidden 속성을 무시하고 False 반환
                    if (dirInfo.Parent == null)
                    {
                        break; // 루트 디렉토리에 도달하면 검사 중단
                    }

                    if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    {
                        return true;
                    }

                    var parentPath = Path.GetDirectoryName(currentPath);
                    if (parentPath == null || parentPath == currentPath)
                    {
                        break;
                    }

                    currentPath = parentPath;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error checking if path is hidden: {ex.Message}");
        }
        return false;
    }

    internal static void CreateDirectory(string path)
    {
        if (Directory.Exists(path)) return;

        var pathParts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var currentPath = pathParts[0];

        for (int i = 1; i < pathParts.Length; i++)
        {
            currentPath = Path.Combine(currentPath, pathParts[i]);
            var directoryInfo = new DirectoryInfo(currentPath);

            if (!directoryInfo.Exists)
            {
                try
                {
                    directoryInfo.Create();
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error creating directory: {ex.Message}");
                    throw;
                }
            }

            if (pathParts[i].StartsWith('.'))
            {
                SetHiddenAttribute(directoryInfo);
            }
        }
    }

    private static void SetHiddenAttribute(DirectoryInfo directoryInfo)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                directoryInfo.Attributes |= FileAttributes.Hidden;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error setting hidden attribute: {ex.Message}");
            }
        }
    }
}