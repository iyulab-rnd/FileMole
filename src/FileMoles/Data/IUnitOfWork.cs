using Microsoft.Data.Sqlite;

namespace FileMoles.Data;

internal interface IRepository<T> where T : class
{
    Task<int> UpsertAsync(T entity, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(T entity, CancellationToken cancellationToken = default);
}

internal interface IUnitOfWork : IDisposable
{
    FileIndexRepository FileIndices { get; }
    TrackingFileRepository TrackingFiles { get; }

    Task<TResult> ExecuteAsync<TResult>(Func<SqliteConnection, Task<TResult>> func, CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<SqliteConnection, Task> func, CancellationToken cancellationToken = default);
    Task OptimizeAsync(CancellationToken cancellationToken);
}