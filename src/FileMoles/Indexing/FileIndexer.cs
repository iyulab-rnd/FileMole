using Microsoft.Data.Sqlite;
using System.IO;
using System.Collections.Concurrent;

namespace FileMoles.Indexing;

public class FileIndexer : IDisposable, IAsyncDisposable
{
    private readonly string _dbPath;
    private SqliteConnection _connection = null!;
    private bool _disposed = false;

    public FileIndexer(string dbPath)
    {
        if (Directory.Exists(Path.GetDirectoryName(dbPath)) is false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        }

        _dbPath = dbPath;
        _connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;");
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
                Attributes INTEGER NOT NULL
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

    public async Task<bool> IndexFileAsync(FileInfo file)
    {
        if (FileMoleUtils.IsHidden(file.FullName))
        {
            return false;
        }

        try
        {
            var fileIndex = new FileIndex(file.FullName)
            {
                Length = file.Length,
                CreationTime = file.CreationTime,
                LastWriteTime = file.LastWriteTime,
                LastAccessTime = file.LastAccessTime,
                Attributes = file.Attributes
            };

            EnsureConnectionOpen();

            using var command = _connection.CreateCommand();
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
            Console.WriteLine($"Error indexing file: {ex.Message}");
            return false;
        }
    }

    public async Task<IEnumerable<FileInfo>> SearchAsync(string searchTerm)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM FileIndex 
                WHERE Name LIKE @SearchTerm OR FullPath LIKE @SearchTerm";
            command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

            var results = new List<FileInfo>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fullPath = reader.GetString(reader.GetOrdinal("FullPath"));
                var file = new FileInfo(fullPath);
                if (file.Exists)
                {
                    // 데이터베이스의 정보로 FileInfo 객체를 업데이트
                    file.Refresh(); // 파일 시스템에서 최신 정보를 가져옴

                    // 데이터베이스의 정보와 파일 시스템의 정보를 비교
                    var dbCreationTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationTime")));
                    var dbLastWriteTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastWriteTime")));
                    var dbLastAccessTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessTime")));
                    var dbAttributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"));
                    var dbLength = reader.GetInt64(reader.GetOrdinal("Size"));

                    if (file.CreationTime != dbCreationTime ||
                        file.LastWriteTime != dbLastWriteTime ||
                        file.LastAccessTime != dbLastAccessTime ||
                        file.Attributes != dbAttributes ||
                        file.Length != dbLength)
                    {
                        // 파일 정보가 변경되었다면 데이터베이스 업데이트
                        await IndexFileAsync(file);
                    }

                    results.Add(file);
                }
                else
                {
                    // 파일이 더 이상 존재하지 않으면 데이터베이스에서 제거
                    await RemoveFileAsync(fullPath);
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching files: {ex.Message}");
            return [];
        }
    }

    public async Task<bool> HasFileChangedAsync(FileInfo file)
    {
        if (file.Exists == false)
        {
            return true;
        }

        var indexedFile = await GetIndexedFileInfoAsync(file.FullName);
        if (indexedFile == null)
        {
            return true;
        }

        return indexedFile.Length != file.Length ||
            indexedFile.CreationTime != file.CreationTime ||
            indexedFile.LastWriteTime != file.LastWriteTime ||
            indexedFile.LastAccessTime != file.LastAccessTime ||
            indexedFile.Attributes != file.Attributes;
    }

    public async Task<FileIndex?> GetIndexedFileInfoAsync(string fullPath)
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
                var file = new FileInfo(reader.GetString(reader.GetOrdinal("FullPath")));
                if (!file.Exists)
                {
                    await RemoveFileAsync(fullPath);
                    return null;
                }

                var fileIndex = new FileIndex(file.FullName)
                {
                    Length = reader.GetInt64(reader.GetOrdinal("Size")),
                    CreationTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationTime"))),
                    LastWriteTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastWriteTime"))),
                    LastAccessTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessTime"))),
                    Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                };

                return fileIndex;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting indexed file info: {ex.Message}");
            return null;
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
    }

    public async Task<int> GetFileCountAsync(string path)
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();

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

    public async Task ClearDatabaseAsync()
    {
        try
        {
            EnsureConnectionOpen();
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex";
            await command.ExecuteNonQueryAsync();
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