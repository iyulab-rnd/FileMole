using FileMoles.Interfaces;
using FileMoles.Storage;
using MimeKit;

namespace FileMoles.Internal;

internal static class FileMoleHelper
{
    internal static bool IsTextFile(string filePath)
    {
        var mimeType = MimeTypes.GetMimeType(filePath);
        return mimeType.StartsWith("text/") ||
               mimeType == "application/json" ||
               mimeType == "application/xml" ||
               mimeType == "application/javascript";
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