using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using FileMole.Storage;
using Microsoft.Data.Sqlite;
using FileMole.Core;

namespace FileMole.Indexing
{
    public class FileIndexer : IFileIndexer, IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;

        public FileIndexer(FileMoleOptions options)
        {
            _dbPath = options.DatabasePath ?? throw new ArgumentNullException(nameof(options.DatabasePath));
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
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
                )";
            command.ExecuteNonQuery();
        }

        public async Task IndexFileAsync(FMFileInfo file)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO FileIndex 
                (Name, FullPath, Size, CreationTime, LastWriteTime, LastAccessTime, Attributes) 
                VALUES (@Name, @FullPath, @Size, @CreationTime, @LastWriteTime, @LastAccessTime, @Attributes)";

            command.Parameters.AddWithValue("@Name", file.Name);
            command.Parameters.AddWithValue("@FullPath", file.FullPath);
            command.Parameters.AddWithValue("@Size", file.Size);
            command.Parameters.AddWithValue("@CreationTime", file.CreationTime.ToString("o"));
            command.Parameters.AddWithValue("@LastWriteTime", file.LastWriteTime.ToString("o"));
            command.Parameters.AddWithValue("@LastAccessTime", file.LastAccessTime.ToString("o"));
            command.Parameters.AddWithValue("@Attributes", (int)file.Attributes);

            await command.ExecuteNonQueryAsync();
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
                results.Add(new FMFileInfo(
                    reader.GetString(reader.GetOrdinal("Name")),
                    reader.GetString(reader.GetOrdinal("FullPath")),
                    reader.GetInt64(reader.GetOrdinal("Size")),
                    DateTime.Parse(reader.GetString(reader.GetOrdinal("CreationTime"))),
                    DateTime.Parse(reader.GetString(reader.GetOrdinal("LastWriteTime"))),
                    DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessTime"))),
                    (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                ));
            }

            return results;
        }

        public async Task<IDictionary<string, int>> GetFileCountByDriveAsync()
        {
            var result = new Dictionary<string, int>();

            using var command = _connection.CreateCommand();
            command.CommandText = @"
                SELECT 
                    SUBSTR(FullPath, 1, INSTR(FullPath, ':')) as Drive,
                    COUNT(*) as FileCount
                FROM FileIndex
                GROUP BY Drive
            ";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var drive = reader.GetString(0);
                var count = reader.GetInt32(1);
                result[drive] = count;
            }

            return result;
        }


        public async Task ClearDatabaseAsync()
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex";
            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}