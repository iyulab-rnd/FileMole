using FileMoles.Data;
using Microsoft.Data.Sqlite;

internal class TrackingFileRepository(DbContext unitOfWork) : IRepository<TrackingFile>
{
    private readonly DbContext _unitOfWork = unitOfWork;

    public static readonly string CreateTableSql = @"
        CREATE TABLE IF NOT EXISTS TrackingFile (
            FullPath TEXT PRIMARY KEY
        );";

    private static TrackingFile BuildTrackingFile(SqliteDataReader reader)
    {
        return new TrackingFile()
        {
            FullPath = reader.GetString(reader.GetOrdinal("FullPath"))
        };
    }

    public async Task<int> UpsertAsync(TrackingFile entity, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO TrackingFile 
                (FullPath) 
                VALUES (@FullPath)";

            command.Parameters.AddWithValue("@FullPath", entity.FullPath);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<TrackingFile?> FindOneAsync(string fullPath, CancellationToken cancellationToken = default)
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

    internal async Task<IEnumerable<TrackingFile>> FindAllAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TrackingFile";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var trackingFiles = new List<TrackingFile>();
            while (await reader.ReadAsync(cancellationToken))
            {
                trackingFiles.Add(BuildTrackingFile(reader));
            }

            return trackingFiles;
        }, cancellationToken);
    }

    public async Task<bool> IsTrackingFileAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        int count = await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingFile WHERE FullPath = @FullPath";
            command.Parameters.AddWithValue("@FullPath", fullPath);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }, cancellationToken);

        return count > 0;
    }
}