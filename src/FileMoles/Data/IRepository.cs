using Microsoft.Data.Sqlite;

namespace FileMoles.Data;

internal interface IRepository<T> where T : class
{
    Task<int> UpsertAsync(T entity, CancellationToken cancellationToken = default);
    Task<int> DeleteAsync(T entity, CancellationToken cancellationToken = default);
}