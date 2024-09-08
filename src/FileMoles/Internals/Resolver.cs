using FileMoles.Data;
using FileMoles.Utils;

namespace FileMoles.Internals;

internal static class Resolver
{
    public static DbContext? DbContext { get; private set; }

    public static DbContext ResolveDbContext(string dbPath)
    {
        if (Directory.Exists(Path.GetDirectoryName(dbPath)) is false)
        {
            IOHelper.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        }
        var dbContext =  new DbContext(dbPath);
        DbContext = dbContext;
        return dbContext;
    }
}
