using Microsoft.Data.Sqlite;

namespace FileMoles.Data;

internal class UnitOfWork : IDisposable
{
    private readonly string _connectionString;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();

    protected UnitOfWork(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    protected async Task InitializeAsync()
    {
        // WAL 모드 설정 및 기타 PRAGMA 설정
        await ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                PRAGMA mmap_size = 30000000000;
                PRAGMA page_size = 32768;";
            await command.ExecuteNonQueryAsync();
        });

        // 테이블 생성
        await ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                {FileIndexRepository.CreateTableSql}
                {TrackingDirRepository.CreateTableSql}";
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<SqliteConnection, Task<TResult>> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DbContext));

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await func(connection);
    }

    public async Task ExecuteAsync(Func<SqliteConnection, Task> func, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(DbContext));

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await func(connection);
    }

    public async Task OptimizeAsync(CancellationToken cancellationToken)
    {
        await ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
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
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
