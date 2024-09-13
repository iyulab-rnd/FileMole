using Microsoft.Data.Sqlite;
using System.Data;

namespace FileMoles.Data;

internal class DbContext : IUnitOfWork
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;
    private bool _disposed;
    private readonly List<Task> _pendingTasks = [];
    private readonly CancellationTokenSource _cts = new();

    public FileIndexRepository FileIndices { get; }
    public TrackingFileRepository TrackingFiles { get; }

    public DbContext(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;");
        _connection.Open();

        FileIndices = new FileIndexRepository(this);
        TrackingFiles = new TrackingFileRepository(this);

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $@"
                {FileIndexRepository.CreateTableSql}
                {TrackingFileRepository.CreateTableSql}";
        command.ExecuteNonQuery();
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        if (_transaction != null)
        {
            throw new InvalidOperationException("A transaction is already in progress.");
        }
        _transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return 0;
        }

        if (_transaction == null)
        {
            throw new InvalidOperationException("No transaction is in progress.");
        }
        await _transaction.CommitAsync(cancellationToken);
        _transaction = null;
        return 1;
    }

    public async Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DbContext));
        }

        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
        return _connection;
    }

    public async Task ExecuteInTransactionAsync(Func<SqliteConnection, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        var task = InternalExecuteInTransactionAsync(action, linkedCts.Token);
        _pendingTasks.Add(task);
        try
        {
            await task;
        }
        finally
        {
            _pendingTasks.Remove(task);
        }
    }

    private async Task InternalExecuteInTransactionAsync(Func<SqliteConnection, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        if (_transaction == null)
        {
            await BeginTransactionAsync(cancellationToken);
            try
            {
                await action(_connection, cancellationToken);
                await SaveChangesAsync(cancellationToken);
            }
            catch
            {
                if (_transaction != null)
                {
                    await _transaction.RollbackAsync(cancellationToken);
                }
                throw;
            }
            finally
            {
                _transaction = null;
            }
        }
        else
        {
            await action(_connection, cancellationToken);
        }
    }

    private async Task WaitForPendingTasksAsync()
    {
        try
        {
            await Task.WhenAll([.. _pendingTasks]);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error while waiting for pending tasks in DbContext: {ex.Message}");
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (!_disposed)
        {
            try
            {
                _cts.Cancel();
            }
            catch (Exception ex)
            {
                Logger.WriteLine($"Error during cancellation: {ex.Message}");
            }

            await WaitForPendingTasksAsync();

            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
            }
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _cts.Dispose();
            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _cts.Cancel();
                WaitForPendingTasksAsync().GetAwaiter().GetResult();
                _transaction?.Dispose();
                _connection.Dispose();
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