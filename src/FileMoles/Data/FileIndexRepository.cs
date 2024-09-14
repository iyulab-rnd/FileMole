using FileMoles;
using FileMoles.Data;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

internal class FileIndexRepository : IRepository<FileIndex>
{
    private readonly DbContext _unitOfWork;

    public static readonly string CreateTableSql = @"
CREATE TABLE IF NOT EXISTS FileIndex (
    Directory TEXT NOT NULL,
    Name TEXT NOT NULL,
    Size INTEGER NOT NULL,
    Created TEXT NOT NULL,
    Modified TEXT NOT NULL,
    Attributes INTEGER NOT NULL,
    LastScanned TEXT NOT NULL,
    PRIMARY KEY (Directory, Name)
);
CREATE INDEX IF NOT EXISTS idx_FileIndex_Directory_Name ON FileIndex(Directory, Name);
CREATE INDEX IF NOT EXISTS idx_FileIndex_Name ON FileIndex(Name);
";

    public FileIndexRepository(DbContext unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<FileIndex?> GetByDirectoryAndNameAsync(string directory, string name, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM FileIndex WHERE Directory = @Directory AND Name = @Name";
            command.Parameters.AddWithValue("@Directory", directory);
            command.Parameters.AddWithValue("@Name", name);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new FileIndex()
                {
                    Directory = reader.GetString(reader.GetOrdinal("Directory")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Size = reader.GetInt64(reader.GetOrdinal("Size")),
                    Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                    Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                    Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                };
            }

            return null;
        }, cancellationToken);
    }

    public async Task<int> UpsertAsync(FileIndex entity, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT OR REPLACE INTO FileIndex 
(Directory, Name, Size, Created, Modified, Attributes, LastScanned) 
VALUES (@Directory, @Name, @Size, @Created, @Modified, @Attributes, @LastScanned)";

            command.Parameters.AddWithValue("@Directory", entity.Directory);
            command.Parameters.AddWithValue("@Name", entity.Name);
            command.Parameters.AddWithValue("@Size", entity.Size);
            command.Parameters.AddWithValue("@Created", entity.Created.ToString("o"));
            command.Parameters.AddWithValue("@Modified", entity.Modified.ToString("o"));
            command.Parameters.AddWithValue("@Attributes", (int)entity.Attributes);
            command.Parameters.AddWithValue("@LastScanned", entity.LastScanned.ToString("o"));

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> UpsertAsync(IEnumerable<FileIndex> entities, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            int rowsAffected = 0;
            try
            {
                using var command = connection.CreateCommand();

                command.CommandText = @"
INSERT OR REPLACE INTO FileIndex 
(Directory, Name, Size, Created, Modified, Attributes, LastScanned) 
VALUES (@Directory, @Name, @Size, @Created, @Modified, @Attributes, @LastScanned)";

                var directoryParam = command.Parameters.Add("@Directory", SqliteType.Text);
                var nameParam = command.Parameters.Add("@Name", SqliteType.Text);
                var sizeParam = command.Parameters.Add("@Size", SqliteType.Integer);
                var createdParam = command.Parameters.Add("@Created", SqliteType.Text);
                var modifiedParam = command.Parameters.Add("@Modified", SqliteType.Text);
                var attributesParam = command.Parameters.Add("@Attributes", SqliteType.Integer);
                var lastScannedParam = command.Parameters.Add("@LastScanned", SqliteType.Text);

                command.Prepare(); // Prepare the statement once

                foreach (var entity in entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    directoryParam.Value = entity.Directory;
                    nameParam.Value = entity.Name;
                    sizeParam.Value = entity.Size;
                    createdParam.Value = entity.Created.ToString("o");
                    modifiedParam.Value = entity.Modified.ToString("o");
                    attributesParam.Value = (int)entity.Attributes;
                    lastScannedParam.Value = entity.LastScanned.ToString("o");

                    rowsAffected += await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                Logger.Error($"Error during UpsertAsync: {ex.Message}");
                throw;
            }

            return rowsAffected;
        }, cancellationToken);
    }

    public async Task<int> DeleteAsync(FileIndex entity, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE Directory = @Directory AND Name = @Name";
            command.Parameters.AddWithValue("@Directory", entity.Directory);
            command.Parameters.AddWithValue("@Name", entity.Name);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteByDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE Directory LIKE @Directory";
            command.Parameters.AddWithValue("@Directory", $"{directory}%");
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteByDirectoryAndNameAsync(string directory, string name, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE Directory = @Directory AND Name = @Name";
            command.Parameters.AddWithValue("@Directory", directory);
            command.Parameters.AddWithValue("@Name", name);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteEntriesNotScannedAfterAsync(DateTime scanStartTime, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM FileIndex WHERE LastScanned < @ScanStartTime";
            command.Parameters.AddWithValue("@ScanStartTime", scanStartTime.ToString("o"));
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<List<FileIndex>> SearchAsync(string search, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
            SELECT * FROM FileIndex 
            WHERE (Directory || '/' || Name) LIKE @Search";
            command.Parameters.AddWithValue("@Search", $"%{search}%");

            var list = new List<FileIndex>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new FileIndex()
                {
                    Directory = reader.GetString(reader.GetOrdinal("Directory")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    Size = reader.GetInt64(reader.GetOrdinal("Size")),
                    Created = DateTime.Parse(reader.GetString(reader.GetOrdinal("Created"))),
                    Modified = DateTime.Parse(reader.GetString(reader.GetOrdinal("Modified"))),
                    Attributes = (FileAttributes)reader.GetInt32(reader.GetOrdinal("Attributes"))
                });
            }
            return list;
        }, cancellationToken);
    }

    public async Task<int> GetCountAsync(string path, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.ExecuteAsync(async connection =>
        {
            using var command = connection.CreateCommand();

            string queryPath = path.TrimEnd('/', '\\');
            queryPath = $"{queryPath}%";

            command.CommandText = @"
            SELECT COUNT(*)
            FROM FileIndex
            WHERE Directory LIKE @Path";
            command.Parameters.AddWithValue("@Path", queryPath);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }, cancellationToken);
    }
}
