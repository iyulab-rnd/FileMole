using FileMoles.Storage;
using MimeKit;

namespace FileMoles.Internals;

internal static class FileMoleUtils
{
    internal static bool IsTextFile(string filePath)
    {
        var mimeType = MimeTypes.GetMimeType(filePath);
        return mimeType.StartsWith("text/") ||
               mimeType == "application/json" ||
               mimeType == "application/xml" ||
               mimeType == "application/javascript";
    }

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

    internal static IStorageProvider CreateStorageProvider(MoleType type, string provider)
    {
        return (type, provider?.ToLower()) switch
        {
            (MoleType.Local, _) => new LocalStorageProvider(),
            (MoleType.Remote, _) => new RemoteStorageProvider(),
            (MoleType.Cloud, "onedrive") => new OneDriveStorageProvider(),
            (MoleType.Cloud, "google") => new GoogleDriveStorageProvider(),
            (MoleType.Cloud, _) => throw new NotSupportedException($"Unsupported cloud provider: {provider}"),
            _ => throw new NotSupportedException($"Unsupported storage type: {type}")
        };
    }
}