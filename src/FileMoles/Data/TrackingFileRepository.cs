using FileMoles.Data;
using Microsoft.Data.Sqlite;

internal class TrackingFileRepository : IRepository<TrackingFile>
{
    private readonly DbContext _unitOfWork;

    public static readonly string CreateTableSql = @"
        CREATE TABLE IF NOT EXISTS TrackingFile (
            FullPath TEXT PRIMARY KEY,
            Hash TEXT NOT NULL UNIQUE,
            LastTrackedTime INTEGER NOT NULL
        );";

    public TrackingFileRepository(DbContext unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private TrackingFile BuildTrackingFile(SqliteDataReader reader)
    {
        return new TrackingFile()
        {
            FullPath = reader.GetString(reader.GetOrdinal("FullPath")),
            Hash = reader.GetString(reader.GetOrdinal("Hash")),
            LastTrackedTime = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(reader.GetOrdinal("LastTrackedTime"))).UtcDateTime
        };
    }

    public async Task<IEnumerable<TrackingFile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TrackingFile";

            var results = new List<TrackingFile>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(BuildTrackingFile(reader));
            }

            return results;
        }, cancellationToken);
    }

    public async Task<int> UpsertAsync(TrackingFile entity, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO TrackingFile 
                (FullPath, Hash, LastTrackedTime) 
                VALUES (@FullPath, @Hash, @LastTrackedTime)";

            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            command.Parameters.AddWithValue("@Hash", entity.Hash);
            command.Parameters.AddWithValue("@LastTrackedTime", new DateTimeOffset(entity.LastTrackedTime).ToUnixTimeSeconds());

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteAsync(TrackingFile entity, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", entity.FullPath);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> UpdateLastTrackedTimeAsync(string fullPath, DateTime lastTrackedTime, CancellationToken cancellationToken = default)
    {
        int rowsAffected = await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE TrackingFile SET LastTrackedTime = @LastTrackedTime WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@LastTrackedTime", new DateTimeOffset(lastTrackedTime).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("@FullPath", fullPath);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

        return rowsAffected > 0;
    }

    public async Task<List<TrackingFile>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM TrackingFile 
                WHERE FullPath LIKE @SearchTerm OR Hash LIKE @SearchTerm";
            command.Parameters.AddWithValue("@SearchTerm", $"%{searchTerm}%");

            var list = new List<TrackingFile>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(BuildTrackingFile(reader));
            }
            return list;
        }, cancellationToken);
    }

    public async Task<TrackingFile?> GetByFullPathAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return BuildTrackingFile(reader);
            }

            return null;
        }, cancellationToken);
    }

    public async Task<TrackingFile?> GetByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TrackingFile WHERE Hash = @Hash";
            command.Parameters.AddWithValue("@Hash", hash);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return BuildTrackingFile(reader);
            }

            return null;
        }, cancellationToken);
    }

    public async Task<bool> IsTrackingFileAsync(string path, CancellationToken cancellationToken = default)
    {
        int count = await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", path);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }, cancellationToken);

        return count > 0;
    }
}