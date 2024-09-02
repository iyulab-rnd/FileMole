using Microsoft.Data.Sqlite;
using FileMoles.Utils;
using System.Collections.Concurrent;

namespace FileMoles.Indexing;

public class FileIndexer : IDisposable, IAsyncDisposable
{
    private readonly string _dbPath;
    private SqliteConnection _connection;
    private readonly HashGenerator _hashGenerator;
    private readonly ConcurrentDictionary<string, (string Hash, DateTime LastWriteTime)> _fileHashCache;
    private bool _disposed = false;

    public FileIndexer(FileMoleOptions options)
    {
        _dbPath = options.DatabasePath ?? Functions.GetDatabasePath();
        _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;");
        _hashGenerator = new HashGenerator();
        _fileHashCache = new ConcurrentDictionary<string, (string, DateTime)>();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS FileIndex (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                FullPath TEXT NOT NULL UNIQUE,
                Size INTEGER NOT NULL,
                CreationTime TEXT NOT NULL,
                LastWriteTime TEXT NOT NULL,
                LastAccessTime TEXT NOT NULL,
                Attributes INTEGER NOT NULL,
                FileHash TEXT,
                LastFileHash TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_FileIndex_FullPath ON FileIndex(FullPath);";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing database: {ex.Message}");
            throw;
        }
    }

    private void EnsureConnectionOpen()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public async Task<bool> IndexFileAsync(FMFileInfo file)
    {
        if (FileMoleUtils.IsHidden(file.FullPath))
        {
            return false;
        }

        try
        {
            EnsureConnectionOpen();

            string fileHash = await GetFileHashAsync(file.FullPath, file.LastWriteTime);
            var existingFile = await GetIndexedFileInfoAsync(file.FullPath);
            string lastFileHash = existingFile?.FileHash ?? string.Empty;

            if (existingFile != null && !string.IsNullOrEmpty(fileHash))
            {
                if (fileHash == existingFile.FileHash &&
                    file.Size == existingFile.Size &&
                    file.LastWriteTime == existingFile.LastWriteTime &&
                    file.LastAccessTime == existingFile.LastAccessTime)
                {
                    return false;
                }
            }

            using var command = _connection.CreateCommand();
            command.CommandText = @"
            INSERT OR REPLACE INTO FileIndex 
            (Name, FullPath, Size, CreationTime, LastWriteTime, LastAccessTime, Attributes, FileHash, LastFileHash) 
            VALUES (@Name, @FullPath, @Size, @CreationTime, @LastWriteTime, @LastAccessTime, @Attributes, @FileHash, @LastFileHash)";

            command.Parameters.AddWithValue("@Name", file.Name);
            command.Parameters.AddWithValue("@FullPath", file.FullPath);
            command.Parameters.AddWithValue("@Size", file.Size);
            command.Parameters.AddWithValue("@CreationTime", file.CreationTime.ToString("o"));
            command.Parameters.AddWithValue("@LastWriteTime", file.LastWriteTime.ToString("o"));
            command.Parameters.AddWithValue("@LastAccessTime", file.LastAccessTime.ToString("o"));
            command.Parameters.AddWithValue("@Attributes", (int)file.Attributes);
            command.Parameters.AddWithValue("@FileHash", fileHash);
            command.Parameters.AddWithValue("@LastFileHash", lastFileHash);

            await command.ExecuteNonQueryAsync();
            _fileHashCache[file.FullPath] = (fileHash, file.LastWriteTime);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error indexing file: {ex.Message}");
            return false;
        }
    }


    public async Task<bool> HasFileChangedAsync(FMFileInfo file)
    {
        try
        {
            // 파일이 숨김 상태인 경우 변경되지 않은 것으로 처리
            if (FileMoleUtils.IsHidden(file.FullPath))
            {
                return false;
            }

            var existingFile = await GetIndexedFileInfoAsync(file.FullPath);
            if (existingFile == null)
            {
                return true;
            }

            bool metadataChanged = file.Size != existingFile.Size ||
                                   file.LastWriteTime != existingFile.LastWriteTime ||
                                   file.LastAccessTime != existingFile.LastAccessTime;

            if (metadataChanged)
            {
                var currentHash = await GetFileHashAsync(file.FullPath, file.LastWriteTime);
                bool contentChanged = currentHash != existingFile.FileHash;

                if (!contentChanged)
                {
                    await UpdateFileMetadataAsync(file, currentHash);
                }

                return contentChanged;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking if file has changed: {ex.Message}");
            return true;
        }
    }

    private async Task UpdateFileMetadataAsync(FMFileInfo file, string currentHash)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                UPDATE FileIndex 
                SET Size = @Size, 
                    LastWriteTime = @LastWriteTime, 
                    LastAccessTime = @LastAccessTime, 
                    FileHash = @FileHash
                WHERE FullPath = @FullPath";

            command.Parameters.AddWithValue("@Size", file.Size);
            command.Parameters.AddWithValue("@LastWriteTime", file.LastWriteTime.ToString("o"));
            command.Parameters.AddWithValue("@LastAccessTime", file.LastAccessTime.ToString("o"));
            command.Parameters.AddWithValue("@FileHash", currentHash);
            command.Parameters.AddWithValue("@FullPath", file.FullPath);

            await command.ExecuteNonQueryAsync();
            _fileHashCache[file.FullPath] = (currentHash, file.LastWriteTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating file metadata: {ex.Message}");
        }
    }

    private async Task<string> GetFileHashAsync(string filePath, DateTime lastWriteTime)
    {
        try
        {
            if (_fileHashCache.TryGetValue(filePath, out var cachedInfo))
            {
                if (cachedInfo.LastWriteTime == lastWriteTime)
                {
                    return cachedInfo.Hash;
                }
            }

            var hash = await _hashGenerator.GenerateHashAsync(filePath);
            _fileHashCache[filePath] = (hash, lastWriteTime);
            return hash;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating file hash: {ex.Message}");
            return string.Empty;
        }
    }

    private FMFileInfo BuildFileInfo(SqliteDataReader reader)
    {
        return FMFileInfo.CreateNew(
            reader.GetString(reader.GetOrdinal("Name")),
            reader.GetString(reader.GetOrdinal("FullPath")),
            reader.GetInt64(reader.GetOrdinal("Size")),
            DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationTime"))),
            DateTime.Parse(reader.GetString(reader.GetOrdinal("LastWriteTime"))),
            DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessTime"))),
            (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes")),
            reader.GetString(reader.GetOrdinal("FileHash")),
            reader.IsDBNull(reader.GetOrdinal("LastFileHash")) ? string.Empty : reader.GetString(reader.GetOrdinal("LastFileHash"))
        );
    }

    public async Task<FMFileInfo?> GetIndexedFileInfoAsync(string fullPath)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileIndex WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var fileInfo = BuildFileInfo(reader);

                if (!File.Exists(fullPath))
                {
                    await RemoveFileAsync(fullPath);
                    return null;
                }

                return fileInfo;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting indexed file info: {ex.Message}");
            return null;
        }
    }

    public async Task<IEnumerable<FMFileInfo>> SearchAsync(string searchTerm)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                    SELECT * FROM FileIndex 
                    WHERE Name LIKE @SearchTerm OR FullPath LIKE @SearchTerm";
            command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

            var results = new List<FMFileInfo>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fileInfo = BuildFileInfo(reader);

                if (File.Exists(fileInfo.FullPath))
                {
                    results.Add(fileInfo);
                }
                else
                {
                    await RemoveFileAsync(fileInfo.FullPath);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching files: {ex.Message}");
            return Enumerable.Empty<FMFileInfo>();
        }
    }

    public async Task RemoveFileAsync(string fullPath)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error removing file from index: {ex.Message}");
        }
        finally
        {
            _fileHashCache.TryRemove(fullPath, out _);
        }
    }

    public async Task<int> GetFileCountAsync(string path)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();

            bool isDrive = path.EndsWith(":") || path.EndsWith(":/") || path.EndsWith(@":\");
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

    public async Task ClearDatabaseAsync()
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex";
            await command.ExecuteNonQueryAsync();
            _fileHashCache.Clear();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error clearing database: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _connection?.Close();
                _connection?.Dispose();
            }

            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _connection = null;
    }
}