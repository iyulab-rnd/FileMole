using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Data.Common;

namespace FileMoles.Data;

internal class DbContext : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed = false;
    private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<int, DbTransaction> _transactions = new();

    public DbContext(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;");
        FileIndexies = new FileIndexContext(this);
        TrackingFiles = new TrackingFileContext(this);
        InitializeDatabase();
    }

    public FileIndexContext FileIndexies { get; }
    public TrackingFileContext TrackingFiles { get; }

    private void EnsureConnectionOpen()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }
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

    internal async Task<SqliteConnection> GetOpenConnectionAsync()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            await _connection.OpenAsync();
        }
        return _connection;
    }

    internal async Task ExecuteInTransactionAsync(Func<SqliteConnection, Task> action)
    {
        await _transactionSemaphore.WaitAsync();
        try
        {
            await using var connection = await GetOpenConnectionAsync();
            var threadId = Environment.CurrentManagedThreadId;

            if (!_transactions.TryGetValue(threadId, out DbTransaction transaction))
            {
                transaction = await connection.BeginTransactionAsync();
                _transactions[threadId] = transaction;
            }

            try
            {
                await action(connection);

                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.CommitAsync();
                }
            }
            catch
            {
                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.RollbackAsync();
                }
                throw;
            }
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    internal async Task<T> ExecuteInTransactionAsync<T>(Func<SqliteConnection, Task<T>> action)
    {
        await _transactionSemaphore.WaitAsync();
        try
        {
            await using var connection = await GetOpenConnectionAsync();
            var threadId = Environment.CurrentManagedThreadId;

            if (!_transactions.TryGetValue(threadId, out DbTransaction transaction))
            {
                transaction = await connection.BeginTransactionAsync();
                _transactions[threadId] = transaction;
            }

            try
            {
                var result = await action(connection);

                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.CommitAsync();
                }

                return result;
            }
            catch
            {
                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.RollbackAsync();
                }
                throw;
            }
        }
        finally
        {
            _transactionSemaphore.Release();
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