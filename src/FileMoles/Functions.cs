namespace FileMoles;

internal static class Constants
{
    public const string FileMoleDatabaseFile = "filemole.db";
    public const string FileMoleTrackConfigFile = "filemole-track-config.json";
    public const string FileMoleIgnoreFile = "filemole-ignore.config";
}

internal static class Functions
{


    internal static string GetDatabasePath(string basePath)
    {
        return Path.Combine(basePath, Constants.FileMoleDatabaseFile);
    }

    internal static string GetTrackConfigPath(string basePath)
    {
        return Path.Combine(basePath, Constants.FileMoleTrackConfigFile);
    }
}
