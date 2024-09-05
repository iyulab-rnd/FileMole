using Microsoft.Data.Sqlite;

namespace FileMoles.Data;

internal class DbContext : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed = false;

    public FileIndexContext FileIndexies { get; }
    public TrackingFileContext TrackingFiles { get; }

    public DbContext(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;");
        FileIndexies = new FileIndexContext(this);
        TrackingFiles = new TrackingFileContext(this);
        InitializeDatabase();
    }

    internal SqliteConnection GetConnection()
    {
        EnsureConnectionOpen();
        return _connection;
    }

    private void InitializeDatabase()
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = FileIndexContext.CreateTableSql + TrackingFileContext.CreateTableSql;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing database: {ex.Message}");
            throw;
        }
    }

    private void EnsureConnectionOpen()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.Close();
                _connection?.Dispose();
            }

            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}