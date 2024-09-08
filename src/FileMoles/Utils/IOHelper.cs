using System.Runtime.InteropServices;

namespace FileMoles.Utils;

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
        // Check if any part of the path starts with a dot
        if (pathParts.Any(part => part.StartsWith('.')))
        {
            return true;
        }

        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            return (fileInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
        }
        else if (Directory.Exists(filePath))
        {
            // Check if any parent directory is hidden
            var currentPath = filePath;
            while (!string.IsNullOrEmpty(currentPath))
            {
                var dirInfo = new DirectoryInfo(currentPath);
                if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    return true;
                }
                currentPath = Path.GetDirectoryName(currentPath);
            }
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
                directoryInfo.Create();
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
            directoryInfo.Attributes |= FileAttributes.Hidden;
        }
    }
}
