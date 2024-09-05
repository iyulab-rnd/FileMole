namespace FileMoles.Data;

internal class FileIndexContext
{
    private readonly DbContext _dbContext;

    internal static readonly string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS FileIndex (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                FullPath TEXT NOT NULL UNIQUE,
                Size INTEGER NOT NULL,
                CreationTime TEXT NOT NULL,
                LastWriteTime TEXT NOT NULL,
                LastAccessTime TEXT NOT NULL,
                Attributes INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_FileIndex_FullPath ON FileIndex(FullPath);";

    public FileIndexContext(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> AddOrUpdateAsync(FileIndex fileIndex)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO FileIndex 
                (Name, FullPath, Size, CreationTime, LastWriteTime, LastAccessTime, Attributes) 
                VALUES (@Name, @FullPath, @Size, @CreationTime, @LastWriteTime, @LastAccessTime, @Attributes)";

            command.Parameters.AddWithValue("@Name", fileIndex.Name);
            command.Parameters.AddWithValue("@FullPath", fileIndex.FullPath);
            command.Parameters.AddWithValue("@Size", fileIndex.Length);
            command.Parameters.AddWithValue("@CreationTime", fileIndex.CreationTime.ToString("o"));
            command.Parameters.AddWithValue("@LastWriteTime", fileIndex.LastWriteTime.ToString("o"));
            command.Parameters.AddWithValue("@LastAccessTime", fileIndex.LastAccessTime.ToString("o"));
            command.Parameters.AddWithValue("@Attributes", (int)fileIndex.Attributes);

            await command.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding or updating file index: {ex.Message}");
            return false;
        }
    }

    public async Task<IEnumerable<FileIndex>> SearchAsync(string searchTerm)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    SELECT * FROM FileIndex 
                    WHERE Name LIKE @SearchTerm OR FullPath LIKE @SearchTerm";
            command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

            var results = new List<FileIndex>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new FileIndex(reader.GetString(reader.GetOrdinal("FullPath")))
                {
                    Length = reader.GetInt64(reader.GetOrdinal("Size")),
                    CreationTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationTime"))),
                    LastWriteTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastWriteTime"))),
                    LastAccessTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessTime"))),
                    Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching files: {ex.Message}");
            return new List<FileIndex>();
        }
    }

    public async Task<FileIndex?> GetAsync(string fullPath)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileIndex WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new FileIndex(reader.GetString(reader.GetOrdinal("FullPath")))
                {
                    Length = reader.GetInt64(reader.GetOrdinal("Size")),
                    CreationTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationTime"))),
                    LastWriteTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastWriteTime"))),
                    LastAccessTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessTime"))),
                    Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting file index: {ex.Message}");
            return null;
        }
    }

    public async Task RemoveAsync(string fullPath)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing file from index: {ex.Message}");
        }
    }

    public async Task<int> GetCountAsync(string path)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();

            bool isDrive = path.EndsWith(':') || path.EndsWith(":/") || path.EndsWith(@":\");
            string queryPath = isDrive ? $"{path}%" : $"{path.TrimEnd('/', '\\')}%";

            command.CommandText = @"
                SELECT COUNT(*)
                FROM FileIndex
                WHERE FullPath LIKE @Path";
            command.Parameters.AddWithValue("@Path", queryPath);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting file count: {ex.Message}");
            return 0;
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex";
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing database: {ex.Message}");
        }
    }
}