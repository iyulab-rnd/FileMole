namespace FileMoles.Diff;

public interface IDiffStrategy
{
    Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath, CancellationToken cancellationToken = default);
}
