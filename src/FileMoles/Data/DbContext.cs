namespace FileMoles.Data;

internal class DbContext : UnitOfWork
{
    public FileIndexRepository FileIndices { get; }
    public TrackingFileRepository TrackingFiles { get; }

    private DbContext(string dbPath) : base(dbPath)
    {
        FileIndices = new FileIndexRepository(this);
        TrackingFiles = new TrackingFileRepository(this);
    }

    public static async Task<DbContext> CreateAsync(string dbPath)
    {
        var dbContext = new DbContext(dbPath);
        await dbContext.InitializeAsync();
        return dbContext;
    }
}