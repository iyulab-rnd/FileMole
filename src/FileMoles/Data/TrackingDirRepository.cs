using FileMoles.Data;
using Microsoft.Data.Sqlite;

internal class TrackingDirRepository : IRepository<TrackingDir>
{
    private readonly DbContext _unitOfWork;

    public static readonly string CreateTableSql = @"
        CREATE TABLE IF NOT EXISTS TrackingDir (
            Path TEXT PRIMARY KEY
        );";

    public TrackingDirRepository(DbContext unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    private TrackingDir BuildTrackingDir(SqliteDataReader reader)
    {
        return new TrackingDir()
        {
            Path = reader.GetString(reader.GetOrdinal("Path"))
        };
    }

    public async Task<int> UpsertAsync(TrackingDir entity, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO TrackingDir 
                (Path) 
                VALUES (@Path)";

            command.Parameters.AddWithValue("@Path", entity.Path);

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteAsync(TrackingDir entity, CancellationToken cancellationToken = default)
    {
        return await DeleteByPathAsync(entity.Path, cancellationToken);
    }

    public async Task<int> DeleteByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM TrackingDir WHERE Path = @Path";
            command.Parameters.AddWithValue("@Path", path);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<TrackingDir?> FindOneAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TrackingDir WHERE Path = @Path";
            command.Parameters.AddWithValue("@Path", path);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return BuildTrackingDir(reader);
            }

            return null;
        }, cancellationToken);
    }

    internal async Task<IEnumerable<TrackingDir>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM TrackingDir";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var trackingFiles = new List<TrackingDir>();
            while (await reader.ReadAsync(cancellationToken))
            {
                trackingFiles.Add(BuildTrackingDir(reader));
            }

            return trackingFiles;
        }, cancellationToken);
    }

    public async Task<bool> IsTrackingPathAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        int count = await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM TrackingDir WHERE Path = @Path";
            command.Parameters.AddWithValue("@Path", directoryPath);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }, cancellationToken);

        return count > 0;
    }
}