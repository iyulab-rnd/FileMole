using Microsoft.Data.Sqlite;
using System.Linq.Expressions;

namespace FileMoles.Data;

internal interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
}

internal interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    FileIndexRepository FileIndices { get; }
    TrackingFileRepository TrackingFiles { get; }
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<SqliteConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<SqliteConnection, CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
