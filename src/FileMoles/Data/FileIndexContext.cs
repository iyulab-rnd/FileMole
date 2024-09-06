using System.Runtime.CompilerServices;

namespace FileMoles.Data;

internal class FileIndexContext(DbContext dbContext)
{
    private readonly DbContext _dbContext = dbContext;

    internal static readonly string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS FileIndex (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                FullPath TEXT NOT NULL UNIQUE,
                Size INTEGER NOT NULL,
                Created TEXT NOT NULL,
                Modified TEXT NOT NULL,
                Attributes INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_FileIndex_FullPath ON FileIndex(FullPath);";

    public async Task<bool> AddOrUpdateAsync(FileIndex fileIndex, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT OR REPLACE INTO FileIndex 
                (Name, FullPath, Size, Created, Modified, Attributes) 
                VALUES (@Name, @FullPath, @Size, @Created, @Modified, @Attributes)";

                command.Parameters.AddWithValue("@Name", fileIndex.Name);
                command.Parameters.AddWithValue("@FullPath", fileIndex.FullPath);
                command.Parameters.AddWithValue("@Size", fileIndex.Size);
                command.Parameters.AddWithValue("@Created", fileIndex.Created.ToString("o"));
                command.Parameters.AddWithValue("@Modified", fileIndex.Modified.ToString("o"));
                command.Parameters.AddWithValue("@Attributes", (int)fileIndex.Attributes);

                await command.ExecuteNonQueryAsync(ct);
            }, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error adding or updating file index: {ex.Message}");
            return false;
        }
    }

    public async Task<List<FileIndex>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken)
    {
        await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM FileIndex 
            WHERE Name LIKE @SearchTerm OR FullPath LIKE @SearchTerm";
        command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

        var list = new List<FileIndex>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new FileIndex(reader.GetString(reader.GetOrdinal("FullPath")))
            {
                Size = reader.GetInt64(reader.GetOrdinal("Size")),
                Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
            };
            list.Add(item);
        }
        return list;
    }

    public async Task<FileIndex?> GetAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileIndex WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new FileIndex(reader.GetString(reader.GetOrdinal("FullPath")))
                {
                    Size = reader.GetInt64(reader.GetOrdinal("Size")),
                    Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                    Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                    Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error getting file index: {ex.Message}");
            return null;
        }
    }

    public async Task RemoveAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM FileIndex WHERE FullPath = @FullPath";
                command.Parameters.AddWithValue("@FullPath", fullPath);
                await command.ExecuteNonQueryAsync(ct);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error removing file from index: {ex.Message}");
        }
    }

    public async Task<int> GetCountAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            bool isDrive = path.EndsWith(':') || path.EndsWith(":/") || path.EndsWith(@":\");
            string queryPath = isDrive ? $"{path}%" : $"{path.TrimEnd('/', '\\')}%";

            command.CommandText = @"
                SELECT COUNT(*)
                FROM FileIndex
                WHERE FullPath LIKE @Path";
            command.Parameters.AddWithValue("@Path", queryPath);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error getting file count: {ex.Message}");
            return 0;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM FileIndex";
                await command.ExecuteNonQueryAsync(ct);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"Error clearing database: {ex.Message}");
        }
    }
}