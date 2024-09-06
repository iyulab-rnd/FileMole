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

    public async Task<bool> AddTrackingFileAsync(TrackingFile trackingFile, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection, ct) =>
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

                await command.ExecuteNonQueryAsync(ct);
            }, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding tracking file: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveTrackingFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.ExecuteInTransactionAsync(async (connection, ct) =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM TrackingFile WHERE FullPath = @FullPath";
                command.Parameters.AddWithValue("@FullPath", fullPath);

                int rowsAffected = await command.ExecuteNonQueryAsync(ct);

                if (rowsAffected == 0)
                {
                    throw new Exception($"No tracking file found with path: {fullPath}");
                }
            }, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing tracking file: {ex.Message}");
            return false;
        }
    }

    public async Task<List<TrackingFile>> GetAllTrackingFilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<TrackingFile>();

            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = @"
            SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime 
            FROM TrackingFile";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
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

    public async Task<TrackingFile?> GetTrackingFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
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

    public async Task<TrackingFile?> GetTrackingFileByBackupFileNameAsync(string backupFileName, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime FROM TrackingFile WHERE BackupFileName = @BackupFileName";
            command.Parameters.AddWithValue("@BackupFileName", backupFileName);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
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

    public async Task<bool> UpdateLastTrackedTimeAsync(string fullPath, DateTime lastTrackedTime, CancellationToken cancellationToken)
    {
        try
        {
            return await _dbContext.ExecuteInTransactionAsync(async (connection, ct) =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE TrackingFile SET LastTrackedTime = @LastTrackedTime WHERE FullPath = @FullPath";
                command.Parameters.AddWithValue("@LastTrackedTime", lastTrackedTime.ToString("o"));
                command.Parameters.AddWithValue("@FullPath", fullPath);
                return await command.ExecuteNonQueryAsync(ct) > 0;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating last tracked time: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> IsTrackingFileAsync(string path, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
        {
            return await IsDirectoryTrackedAsync(path, cancellationToken);
        }
        else if (File.Exists(path))
        {
            return await IsFileTrackedAsync(path, cancellationToken);
        }
        else
        {
            return false;
        }
    }

    private async Task<bool> IsDirectoryTrackedAsync(string directoryPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath AND IsDirectory = 1";
            command.Parameters.AddWithValue("@FullPath", directoryPath);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking if directory is tracked: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> IsFileTrackedAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath AND IsDirectory = 0";
            command.Parameters.AddWithValue("@FullPath", filePath);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking if file is tracked: {ex.Message}");
            return false;
        }
    }

    public async Task<List<string>> GetTrackedFilesInDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dbContext.GetOpenConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT FullPath FROM TrackingFile WHERE FullPath LIKE @DirectoryPath AND IsDirectory = 0";
            command.Parameters.AddWithValue("@DirectoryPath", directoryPath + "%");
            var results = new List<string>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
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