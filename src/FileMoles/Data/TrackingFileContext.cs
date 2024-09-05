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
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO TrackingFile 
                (BackupFileName, FullPath, IsDirectory, LastTrackedTime) 
                VALUES (@BackupFileName, @FullPath, @IsDirectory, @LastTrackedTime)";
            command.Parameters.AddWithValue("@BackupFileName", trackingFile.BackupFileName);
            command.Parameters.AddWithValue("@FullPath", trackingFile.FullPath);
            command.Parameters.AddWithValue("@IsDirectory", trackingFile.IsDirectory ? 1 : 0);
            command.Parameters.AddWithValue("@LastTrackedTime", trackingFile.LastTrackedTime.ToString("o"));
            return await command.ExecuteNonQueryAsync() > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error adding tracking file: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RemoveTrackingFileAsync(string fullPath)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            return await command.ExecuteNonQueryAsync() > 0;
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
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BackupFileName, FullPath, IsDirectory, LastTrackedTime FROM TrackingFile";
            var results = new List<TrackingFile>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var trackingFile = new TrackingFile(reader.GetString(2), reader.GetInt32(3) == 1)
                {
                    Id = reader.GetInt32(0),
                    BackupFileName = reader.GetString(1),
                    LastTrackedTime = DateTime.Parse(reader.GetString(4))
                };
                results.Add(trackingFile);
            }
            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting tracking files: {ex.Message}");
            return new List<TrackingFile>();
        }
    }

    public async Task<TrackingFile?> GetTrackingFileAsync(string fullPath)
    {
        try
        {
            using var connection = _dbContext.GetConnection();
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
            using var connection = _dbContext.GetConnection();
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
            using var connection = _dbContext.GetConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE TrackingFile SET LastTrackedTime = @LastTrackedTime WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@LastTrackedTime", lastTrackedTime.ToString("o"));
            command.Parameters.AddWithValue("@FullPath", fullPath);
            return await command.ExecuteNonQueryAsync() > 0;
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
            using var connection = _dbContext.GetConnection();
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
            using var connection = _dbContext.GetConnection();
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
            using var connection = _dbContext.GetConnection();
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
            return new List<string>();
        }
    }
}