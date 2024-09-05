namespace FileMoles.Internals;

internal static class Functions
{
    internal static string GetDatabasePath(string basePath)
    {
        return Path.Combine(basePath, Constants.FileMoleDatabaseFile);
    }
}