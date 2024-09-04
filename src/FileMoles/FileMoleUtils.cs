using FileMoles.Storage;
using MimeKit;

namespace FileMoles;

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
        var file = new FileInfo(filePath);
        // 파일이 숨김 속성을 가지거나 파일/폴더명이 '.'으로 시작하는 경우 숨김으로 처리
        if ((file.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden ||
            file.Name.StartsWith('.'))
        {
            return true;
        }

        // 경로의 모든 부분을 검사하여 숨김 폴더 내에 있는지 확인
        string[] pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string part in pathParts)
        {
            if (part.StartsWith('.'))
            {
                return true;
            }
        }

        return false;
    }

    internal static async Task CopyFileAsync(string sourceFile, string destinationFile)
    {
        using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        using var destinationStream = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await sourceStream.CopyToAsync(destinationStream);
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