using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Data.Common;

namespace FileMoles.Data;

internal class DbContext : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _transactionSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<int, DbTransaction> _transactions = new();
    private bool _disposed = false;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);

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
            Logger.WriteLine($"Error initializing database: {ex.Message}");
            throw;
        }
    }

    internal async Task<SqliteConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        // Ensure the connection is properly opened
        if (_connection.State == System.Data.ConnectionState.Closed)
        {
            try
            {
                await _connection.OpenAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Error opening database connection: {ex.Message}");
                throw;
            }
        }
        return _connection;
    }

    internal async Task ExecuteInTransactionAsync(Func<SqliteConnection, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await _transactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await GetOpenConnectionAsync(cancellationToken);
            var threadId = Environment.CurrentManagedThreadId;

            if (!_transactions.TryGetValue(threadId, out var transaction))
            {
                transaction = await connection.BeginTransactionAsync(cancellationToken);
                _transactions[threadId] = transaction;
            }

            try
            {
                await action(connection, cancellationToken);

                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.CommitAsync(cancellationToken);
                }
            }
            catch
            {
                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.RollbackAsync(cancellationToken);
                }
                throw;
            }
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    internal async Task<T> ExecuteInTransactionAsync<T>(Func<SqliteConnection, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _transactionSemaphore.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await GetOpenConnectionAsync(cancellationToken);
            var threadId = Environment.CurrentManagedThreadId;

            if (!_transactions.TryGetValue(threadId, out var transaction))
            {
                transaction = await connection.BeginTransactionAsync(cancellationToken);
                _transactions[threadId] = transaction;
            }

            try
            {
                var result = await action(connection, cancellationToken);

                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.CommitAsync(cancellationToken);
                }

                return result;
            }
            catch
            {
                if (_transactions.TryRemove(threadId, out var removedTransaction))
                {
                    await removedTransaction.RollbackAsync(cancellationToken);
                }
                throw;
            }
        }
        finally
        {
            _transactionSemaphore.Release();
        }
    }

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _disposeLock.Wait();
            try
            {
                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                }
            }
            finally
            {
                _disposeLock.Release();
            }
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeLock.WaitAsync();
        try
        {
            if (_disposed)
                return;

            if (_connection != null)
            {
                await _connection.CloseAsync();
                await _connection.DisposeAsync();
            }

            _disposed = true;
        }
        finally
        {
            _disposeLock.Release();
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
