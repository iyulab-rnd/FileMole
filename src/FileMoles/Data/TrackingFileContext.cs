namespace FileMoles.Data;

internal class TrackingFileContext
{
    private readonly DbContext _dbContext;

    internal static readonly string CreateTableSql = @"
        CREATE TABLE IF NOT EXISTS TrackingFile (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            BackupFileName TEXT NOT NULL UNIQUE,
            FullPath TEXT NOT NULL UNIQUE,
            IsDirectory INTEGER NOT NULL,
            LastTrackedTime TEXT NOT NULL
        );";

    public TrackingFileContext(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> AddTrackingFileAsync(TrackingFile trackingFile)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = @"
            INSERT OR REPLACE INTO TrackingFile 
            (BackupFileName, FullPath, IsDirectory, LastTrackedTime) 
            VALUES (@BackupFileName, @FullPath, @IsDirectory, @LastTrackedTime)";

                command.Parameters.AddWithValue("@BackupFileName", trackingFile.BackupFileName);
                command.Parameters.AddWithValue("@FullPath", trackingFile.FullPath);
                command.Parameters.AddWithValue("@IsDirectory", trackingFile.IsDirectory ? 1 : 0);
                command.Parameters.AddWithValue("@LastTrackedTime", trackingFile.LastTrackedTime.ToString("o"));

                await command.ExecuteNonQueryAsync();
            });

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding tracking file: {ex.Message}");
            // 여기서 예외를 다시 throw하지 않고 false를 반환합니다.
            // 만약 호출자가 예외 처리를 원한다면, throw를 유지할 수 있습니다.
            return false;
        }
    }

    public async Task<bool> RemoveTrackingFileAsync(string fullPath)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM TrackingFile WHERE FullPath = @FullPath";
                command.Parameters.AddWithValue("@FullPath", fullPath);

                int rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    throw new Exception($"No tracking file found with path: {fullPath}");
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing tracking file: {ex.Message}");
            return false;
        }
    }

    public async Task<List<TrackingFile>> GetAllTrackingFilesAsync()
    {
        try
        {
            var results = new List<TrackingFile>();

            await using var connection = await _dbContext.GetOpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = @"
            SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime 
            FROM TrackingFile";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var trackingFile = new TrackingFile(
                    reader.GetString(reader.GetOrdinal("FullPath")),
                    reader.GetInt32(reader.GetOrdinal("IsDirectory")) == 1)
                {
                    Id = reader.GetInt32(reader.GetOrdinal("Id")),
                    BackupFileName = reader.GetString(reader.GetOrdinal("BackupFileName")),
                    LastTrackedTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastTrackedTime")))
                };
                results.Add(trackingFile);
            }

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting all tracking files: {ex.Message}");
            return [];
        }
    }

    public async Task<TrackingFile?> GetTrackingFileAsync(string fullPath)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TrackingFile(reader.GetString(2), reader.GetInt32(3) == 1)
                {
                    Id = reader.GetInt32(0),
                    BackupFileName = reader.GetString(1),
                    LastTrackedTime = DateTime.Parse(reader.GetString(4))
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting tracking file: {ex.Message}");
            return null;
        }
    }

    public async Task<TrackingFile?> GetTrackingFileByBackupFileNameAsync(string backupFileName)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime FROM TrackingFile WHERE BackupFileName = @BackupFileName";
            command.Parameters.AddWithValue("@BackupFileName", backupFileName);
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new TrackingFile(reader.GetString(2), reader.GetInt32(3) == 1)
                {
                    Id = reader.GetInt32(0),
                    BackupFileName = reader.GetString(1),
                    LastTrackedTime = DateTime.Parse(reader.GetString(4))
                };
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting tracking file by backup file name: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UpdateLastTrackedTimeAsync(string fullPath, DateTime lastTrackedTime)
    {
        try
        {
            return await _dbContext.ExecuteInTransactionAsync(async (connection) =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE TrackingFile SET LastTrackedTime = @LastTrackedTime WHERE FullPath = @FullPath";
                command.Parameters.AddWithValue("@LastTrackedTime", lastTrackedTime.ToString("o"));
                command.Parameters.AddWithValue("@FullPath", fullPath);
                return await command.ExecuteNonQueryAsync() > 0;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating last tracked time: {ex.Message}");
            return false;
        }
    }

    public Task<bool> IsTrackingFileAsync(string path)
    {
        if (Directory.Exists(path))
        {
            return IsDirectoryTrackedAsync(path);
        }
        else if (File.Exists(path))
        {
            return IsFileTrackedAsync(path);
        }
        else
        {
            return Task.FromResult(false);
        }
    }

    private async Task<bool> IsDirectoryTrackedAsync(string directoryPath)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath AND IsDirectory = 1";
            command.Parameters.AddWithValue("@FullPath", directoryPath);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking if directory is tracked: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> IsFileTrackedAsync(string filePath)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath AND IsDirectory = 0";
            command.Parameters.AddWithValue("@FullPath", filePath);
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking if file is tracked: {ex.Message}");
            return false;
        }
    }

    public async Task<List<string>> GetTrackedFilesInDirectoryAsync(string directoryPath)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync();
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting tracked files in directory: {ex.Message}");
            return [];
        }
    }
}