using FileMoles.Data;

internal class FileIndexRepository : IRepository<FileIndex>
{
    private readonly DbContext _unitOfWork;

    public static readonly string CreateTableSql = @"
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

    public FileIndexRepository(DbContext unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<FileIndex?> GetByIdAsync(int id)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FileIndex WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FileIndex()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Size = reader.GetInt64(reader.GetOrdinal("Size")),
                Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
            };
        }

        return null;
    }

    public async Task<FileIndex?> GetByFullPathAsync(string fullPath)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FileIndex WHERE FullPath = @FullPath";
        command.Parameters.AddWithValue("@FullPath", fullPath);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new FileIndex()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Size = reader.GetInt64(reader.GetOrdinal("Size")),
                Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
            };
        }

        return null;
    }

    public async Task<IEnumerable<FileIndex>> GetAllAsync()
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM FileIndex";

        var results = new List<FileIndex>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new FileIndex()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Size = reader.GetInt64(reader.GetOrdinal("Size")),
                Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
            });
        }

        return results;
    }

    public async Task<IEnumerable<FileIndex>> FindAsync(System.Linq.Expressions.Expression<Func<FileIndex, bool>> predicate)
    {
        // This method is not directly implementable with raw SQL.
        // We'll implement a basic search functionality instead.
        return await SearchAsync(predicate.ToString());
    }

    public async Task AddAsync(FileIndex entity)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    INSERT INTO FileIndex 
                    (Name, FullPath, Size, Created, Modified, Attributes) 
                    VALUES (@Name, @FullPath, @Size, @Created, @Modified, @Attributes)";

            command.Parameters.AddWithValue("@Name", entity.Name);
            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            command.Parameters.AddWithValue("@Size", entity.Size);
            command.Parameters.AddWithValue("@Created", entity.Created.ToString("o"));
            command.Parameters.AddWithValue("@Modified", entity.Modified.ToString("o"));
            command.Parameters.AddWithValue("@Attributes", (int)entity.Attributes);

            await command.ExecuteNonQueryAsync(ct);
        });
    }

    public async Task UpdateAsync(FileIndex entity)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    UPDATE FileIndex 
                    SET Name = @Name, Size = @Size, Created = @Created, Modified = @Modified, Attributes = @Attributes
                    WHERE FullPath = @FullPath";

            command.Parameters.AddWithValue("@Name", entity.Name);
            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            command.Parameters.AddWithValue("@Size", entity.Size);
            command.Parameters.AddWithValue("@Created", entity.Created.ToString("o"));
            command.Parameters.AddWithValue("@Modified", entity.Modified.ToString("o"));
            command.Parameters.AddWithValue("@Attributes", (int)entity.Attributes);

            await command.ExecuteNonQueryAsync(ct);
        });
    }

    public Task DeleteAsync(FileIndex entity)
    {
        return DeleteByPathAsync(entity.FullPath);
    }

    public async Task DeleteByPathAsync(string fullPath)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            await command.ExecuteNonQueryAsync(ct);
        });
    }

    public async Task<List<FileIndex>> SearchAsync(string searchTerm)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
                SELECT * FROM FileIndex 
                WHERE FullPath LIKE @SearchTerm";
        command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

        var list = new List<FileIndex>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new FileIndex()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Name = reader.GetString(reader.GetOrdinal("Name")),
                Size = reader.GetInt64(reader.GetOrdinal("Size")),
                Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
            });
        }
        return list;
    }

    public async Task<int> GetCountAsync(string path)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
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

    public async Task ClearAsync()
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex";
            await command.ExecuteNonQueryAsync(ct);
        });
    }
}
