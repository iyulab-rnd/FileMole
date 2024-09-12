using FileMoles.Data;

internal class TrackingFileRepository : IRepository<TrackingFile>
{
    private readonly DbContext _unitOfWork;

    public static readonly string CreateTableSql = @"
            CREATE TABLE IF NOT EXISTS TrackingFile (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Hash TEXT NOT NULL UNIQUE,
                FullPath TEXT NOT NULL UNIQUE,
                IsDirectory INTEGER NOT NULL,
                LastTrackedTime TEXT NOT NULL
            );";

    public TrackingFileRepository(DbContext unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TrackingFile?> GetByIdAsync(int id)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TrackingFile WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new TrackingFile()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                IsDirectory = reader.GetBoolean(reader.GetOrdinal("IsDirectory")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Hash = reader.GetString(reader.GetOrdinal("Hash")),
                LastTrackedTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastTrackedTime")))
            };
        }

        return null;
    }

    public async Task<IEnumerable<TrackingFile>> GetAllAsync()
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TrackingFile";

        var results = new List<TrackingFile>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TrackingFile()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                IsDirectory = reader.GetBoolean(reader.GetOrdinal("IsDirectory")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Hash = reader.GetString(reader.GetOrdinal("Hash")),
                LastTrackedTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastTrackedTime")))
            });
        }

        return results;
    }

    public async Task<IEnumerable<TrackingFile>> FindAsync(System.Linq.Expressions.Expression<Func<TrackingFile, bool>> predicate)
    {
        // This method is not directly implementable with raw SQL.
        // We'll implement a basic search functionality instead.
        return await SearchAsync(predicate.ToString());
    }

    public async Task AddAsync(TrackingFile entity)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    INSERT OR REPLACE INTO TrackingFile 
                    (Hash, FullPath, IsDirectory, LastTrackedTime) 
                    VALUES (@Hash, @FullPath, @IsDirectory, @LastTrackedTime)";

            command.Parameters.AddWithValue("@Hash", entity.Hash);
            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            command.Parameters.AddWithValue("@IsDirectory", entity.IsDirectory ? 1 : 0);
            command.Parameters.AddWithValue("@LastTrackedTime", entity.LastTrackedTime.ToString("o"));

            await command.ExecuteNonQueryAsync(ct);
        });
    }

    public async Task UpdateAsync(TrackingFile entity)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                    UPDATE TrackingFile 
                    SET Hash = @Hash, IsDirectory = @IsDirectory, LastTrackedTime = @LastTrackedTime
                    WHERE FullPath = @FullPath";

            command.Parameters.AddWithValue("@Hash", entity.Hash);
            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            command.Parameters.AddWithValue("@IsDirectory", entity.IsDirectory ? 1 : 0);
            command.Parameters.AddWithValue("@LastTrackedTime", entity.LastTrackedTime.ToString("o"));

            await command.ExecuteNonQueryAsync(ct);
        });
    }

    public async Task DeleteAsync(TrackingFile entity)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            await command.ExecuteNonQueryAsync(ct);
        });
    }

    public async Task<List<TrackingFile>> SearchAsync(string searchTerm)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
                SELECT * FROM TrackingFile 
                WHERE FullPath LIKE @SearchTerm OR Hash LIKE @SearchTerm";
        command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

        var list = new List<TrackingFile>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TrackingFile()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                IsDirectory = reader.GetBoolean(reader.GetOrdinal("IsDirectory")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Hash = reader.GetString(reader.GetOrdinal("Hash")),
                LastTrackedTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastTrackedTime")))
            });
        }
        return list;
    }

    public async Task<TrackingFile?> GetByFullPathAsync(string fullPath)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TrackingFile WHERE FullPath = @FullPath";
        command.Parameters.AddWithValue("@FullPath", fullPath);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new TrackingFile()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                IsDirectory = reader.GetBoolean(reader.GetOrdinal("IsDirectory")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Hash = reader.GetString(reader.GetOrdinal("Hash")),
                LastTrackedTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastTrackedTime")))
            };
        }

        return null;
    }

    public async Task<TrackingFile?> GetByHashAsync(string hash)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM TrackingFile WHERE Hash = @Hash";
        command.Parameters.AddWithValue("@Hash", hash);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new TrackingFile()
            {
                FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
                IsDirectory = reader.GetBoolean(reader.GetOrdinal("IsDirectory")),
                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                Hash = reader.GetString(reader.GetOrdinal("Hash")),
                LastTrackedTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastTrackedTime")))
            };
        }

        return null;
    }

    public async Task<bool> IsTrackingFileAsync(string path)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath";
        command.Parameters.AddWithValue("@FullPath", path);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task<List<string>> GetTrackedFilesInDirectoryAsync(string directoryPath)
    {
        var connection = await _unitOfWork.GetConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT FullPath FROM TrackingFile WHERE FullPath LIKE @DirectoryPath AND IsDirectory = 0";
        command.Parameters.AddWithValue("@DirectoryPath", directoryPath + "%");
        var results = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }

    public async Task<bool> UpdateLastTrackedTimeAsync(string fullPath, DateTime lastTrackedTime)
    {
        int rowsAffected = 0;
        await _unitOfWork.ExecuteInTransactionAsync(async (connection, ct) =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE TrackingFile SET LastTrackedTime = @LastTrackedTime WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@LastTrackedTime", lastTrackedTime.ToString("o"));
            command.Parameters.AddWithValue("@FullPath", fullPath);
            rowsAffected = await command.ExecuteNonQueryAsync(ct);
        });

        return rowsAffected > 0;
    }
}