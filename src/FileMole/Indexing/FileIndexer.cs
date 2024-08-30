using FileMole.Storage;
using Microsoft.Data.Sqlite;
using FileMole.Core;
using FileMole.Utils;
using System.Collections.Concurrent;

namespace FileMole.Indexing;

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

    private void EnsureConnectionOpen()
    {
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public async Task<bool> IndexFileAsync(FMFileInfo file)
    {
        EnsureConnectionOpen();
        string fileHash = string.Empty;
        bool isHashFailed = false;

        try
        {
            fileHash = await GetFileHashAsync(file.FullPath, file.LastWriteTime);

            // 해시가 빈 문자열이라면, 해시 생성 실패로 간주
            if (string.IsNullOrEmpty(fileHash))
            {
                isHashFailed = true;
            }
        }
        catch (Exception ex)
        {
            // 해시 생성 실패 시 예외 처리 및 로그
            Console.WriteLine($"해시 생성 실패: {file.FullPath}, 오류: {ex.Message}");
            isHashFailed = true;
        }

        var existingFile = await GetIndexedFileInfoAsync(file.FullPath);
        string lastFileHash = existingFile?.FileHash ?? string.Empty;

        if (isHashFailed)
        {
            fileHash = string.Empty; // 현재 해시 비움
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
        return true; // 파일이 업데이트됨
    }


    public async Task<bool> HasFileChangedAsync(FMFileInfo file)
    {
        var existingFile = await GetIndexedFileInfoAsync(file.FullPath);
        if (existingFile == null)
        {
            return true; // 인덱스에 없는 파일은 변경된 것으로 간주
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
                // 메타데이터만 변경된 경우, 인덱스 업데이트
                await UpdateFileMetadataAsync(file, currentHash);
            }

            return contentChanged;
        }

        return false;
    }

    private async Task UpdateFileMetadataAsync(FMFileInfo file, string currentHash)
    {
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

    private async Task<string> GetFileHashAsync(string filePath, DateTime lastWriteTime)
    {
        try
        {
            // 캐시된 해시가 있고 파일이 변경되지 않았다면 캐시된 해시 반환
            if (_fileHashCache.TryGetValue(filePath, out var cachedInfo))
            {
                if (cachedInfo.LastWriteTime == lastWriteTime)
                {
                    return cachedInfo.Hash;
                }
            }

            // 해시 생성
            var hash = await _hashGenerator.GenerateHashAsync(filePath);
            _fileHashCache[filePath] = (hash, lastWriteTime);
            return hash;
        }
        catch (Exception)
        {
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
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM FileIndex WHERE FullPath = @FullPath";
        command.Parameters.AddWithValue("@FullPath", fullPath);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var fileInfo = BuildFileInfo(reader);

            // 실제 파일 시스템에서 파일 존재 여부 확인
            if (!File.Exists(fullPath))
            {
                // 파일이 실제로 존재하지 않으면 인덱스에서 제거
                await RemoveFileAsync(fullPath);
                return null;
            }

            return fileInfo;
        }

        return null;
    }

    public async Task<IEnumerable<FMFileInfo>> SearchAsync(string searchTerm)
    {
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

            // 실제 파일 시스템에서 파일 존재 여부 확인
            if (File.Exists(fileInfo.FullPath))
            {
                results.Add(fileInfo);
            }
            else
            {
                // 파일이 실제로 존재하지 않으면 인덱스에서 제거
                await RemoveFileAsync(fileInfo.FullPath);
            }
        }

        return results;
    }

    public async Task RemoveFileAsync(string fullPath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM FileIndex WHERE FullPath = @FullPath";
        command.Parameters.AddWithValue("@FullPath", fullPath);
        await command.ExecuteNonQueryAsync();
        _fileHashCache.TryRemove(fullPath, out _);
    }

    public async Task<int> GetFileCountAsync(string path)
    {
        EnsureConnectionOpen();

        using var command = _connection.CreateCommand();

        // 드라이브인지 폴더인지에 따라 경로 확인 및 쿼리 작성
        bool isDrive = path.EndsWith(":") || path.EndsWith(":/") || path.EndsWith(@":\");
        string queryPath = isDrive ? $"{path}%" : $"{path.TrimEnd('/', '\\')}%"; // 드라이브나 폴더에 따라 쿼리 경로 설정

        command.CommandText = @"
        SELECT COUNT(*)
        FROM FileIndex
        WHERE FullPath LIKE @Path";
        command.Parameters.AddWithValue("@Path", queryPath);

        var result = await command.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task ClearDatabaseAsync()
    {
        EnsureConnectionOpen();
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM FileIndex";
        await command.ExecuteNonQueryAsync();
        _fileHashCache.Clear();
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