using Microsoft.Data.Sqlite;
using System.Data;
using System.Threading;

namespace FileMoles.Data;

internal class DbContext : IUnitOfWork
{
    private readonly string _connectionString;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();

    public FileIndexRepository FileIndices { get; }
    public TrackingFileRepository TrackingFiles { get; }

    private DbContext(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;";

        FileIndices = new FileIndexRepository(this);
        TrackingFiles = new TrackingFileRepository(this);
    }

    public static async Task<DbContext> CreateAsync(string dbPath)
    {
        var dbContext = new DbContext(dbPath);
        await dbContext.InitializeAsync();
        return dbContext;
    }

    private async Task InitializeAsync()
    {
        await ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                    {FileIndexRepository.CreateTableSql}
                    {TrackingFileRepository.CreateTableSql}";
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<SqliteConnection, Task<TResult>> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DbContext));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await func(connection);
    }

    public async Task ExecuteAsync(Func<SqliteConnection, Task> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DbContext));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await func(connection);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
