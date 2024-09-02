namespace FileMoles;

internal static class Functions
{
    internal static string GetFileMoleDataPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileMole");
    }

    internal static string GetDatabasePath()
    {
        return Path.Combine(GetFileMoleDataPath(), "filemole.db");
    }

    internal static string GetTrackConfigPath()
    {
        return Path.Combine(GetFileMoleDataPath(), "track-config.json");
    }
}
