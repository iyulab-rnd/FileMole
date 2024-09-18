using FileMoles.Data;
using FileMoles.Indexing;
using FileMoles.Interfaces;
using FileMoles.Internal;

namespace FileMoles.Monitoring;

internal class InitialScanner(DbContext dbContext, FileIndexer fileIndexer, Dictionary<string, IStorageProvider> storageProviders)
{
    private readonly DbContext _dbContext = dbContext;
    private readonly FileIndexer _fileIndexer = fileIndexer;
    private readonly Dictionary<string, IStorageProvider> _storageProviders = storageProviders;
    private readonly int _degreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2);

    public async Task ScanAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var scanStartTime = DateTime.UtcNow;

        try
        {
            await Parallel.ForEachAsync(paths,
                new ParallelOptions { MaxDegreeOfParallelism = _degreeOfParallelism, CancellationToken = cancellationToken },
                async (path, ct) => await ScanDirectoryAsync(path, ct));

            await _fileIndexer.RemoveEntriesNotScannedAfterAsync(scanStartTime, cancellationToken);
            await _dbContext.OptimizeAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 작업이 취소되었으므로 추가 처리가 필요 없습니다.
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error during initial scan");
            throw;
        }
    }

    private async Task ScanDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var storageProvider = GetStorageProviderForPath(path)
                ?? throw new ArgumentException($"No storage provider found for path: {path}");

            var filesOnDisk = new List<FileInfo>();
            var directories = new List<string>();

            // 현재 디렉터리의 파일 및 서브디렉터리 목록을 가져옵니다.
            await foreach (var file in storageProvider.GetFilesAsync(path))
            {
                if (!IOHelper.IsHidden(file.FullName))
                {
                    filesOnDisk.Add(file);
                }
            }

            await foreach (var directory in storageProvider.GetDirectoriesAsync(path))
            {
                if (!IOHelper.IsHidden(directory.FullName))
                {
                    directories.Add(directory.FullName);
                }
            }

            // 데이터베이스에서 현재 디렉터리의 파일 인덱스를 가져옵니다.
            var fileIndexEntries = await _fileIndexer.GetFileIndicesByDirectoryAsync(path, cancellationToken);

            // 파일 시스템과 데이터베이스의 파일 목록을 비교하고 변경 사항을 처리합니다.
            await IndexFilesAsync(filesOnDisk, fileIndexEntries, cancellationToken);

            // 서브디렉터리에 대해 재귀적으로 스캔을 수행합니다.
            foreach (var dir in directories)
            {
                await ScanDirectoryAsync(dir, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error scanning directory: {path}");
        }
    }

    private async Task IndexFilesAsync(List<FileInfo> filesOnDisk, List<FileIndex> fileIndexEntries, CancellationToken cancellationToken)
    {
        var unchangedIndices = new List<FileIndex>();
        var changedIndices = new List<FileIndex>();
        var newIndices = new List<FileIndex>();
        var deletedIndices = new List<FileIndex>();

        var fileIndexDict = fileIndexEntries.ToDictionary(f => f.Name, f => f);

        foreach (var file in filesOnDisk)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fileIndexDict.TryGetValue(file.Name, out var existingIndex))
            {
                existingIndex.LastScanned = DateTime.UtcNow;
                if (existingIndex.IsChanged(file))
                {
                    existingIndex.Size = file.Length;
                    existingIndex.Created = file.CreationTimeUtc;
                    existingIndex.Modified = file.LastWriteTimeUtc;
                    existingIndex.Attributes = file.Attributes;
                    changedIndices.Add(existingIndex);
                }
                else
                {
                    unchangedIndices.Add(existingIndex);
                }
                fileIndexDict.Remove(file.Name);
            }
            else
            {
                newIndices.Add(FileIndex.CreateNew(file));
            }
        }

        // 남아있는 항목들은 삭제된 파일입니다.
        deletedIndices.AddRange(fileIndexDict.Values);

        // 배치 업데이트 수행
        if (unchangedIndices.Count > 0)
        {
            await _fileIndexer.UpdateLastScannedAsync(unchangedIndices, cancellationToken);
        }

        if (changedIndices.Count > 0 || newIndices.Count > 0)
        {
            var indicesToUpsert = changedIndices.Concat(newIndices).ToList();
            await _fileIndexer.UpsertFileIndicesAsync(indicesToUpsert, cancellationToken);
        }

        if (deletedIndices.Count > 0)
        {
            await _fileIndexer.DeleteFileIndicesAsync(deletedIndices, cancellationToken);
        }
    }

    private IStorageProvider? GetStorageProviderForPath(string path)
    {
        return _storageProviders
            .Where(kvp => path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kvp => kvp.Key.Length)
            .Select(kvp => kvp.Value)
            .FirstOrDefault();
    }
}