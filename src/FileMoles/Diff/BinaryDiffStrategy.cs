﻿namespace FileMoles.Diff;

public class BinaryDiffResult : DiffResult
{
    public long OldFileSize { get; set; }
    public long NewFileSize { get; set; }
    public string? OldFileHash { get; set; }
    public string? NewFileHash { get; set; }
    public bool AreIdentical { get; set; }
}

public class BinaryDiffStrategy : IDiffStrategy
{
    public async Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath, CancellationToken cancellationToken = default)
    {
        var oldHash = await CalculateFileHashAsync(oldFilePath, cancellationToken);
        var newHash = await CalculateFileHashAsync(newFilePath, cancellationToken);

        var result = new BinaryDiffResult
        {
            FileType = "Binary",
            OldFileSize = new FileInfo(oldFilePath).Length,
            NewFileSize = new FileInfo(newFilePath).Length,
            OldFileHash = oldHash,
            NewFileHash = newHash,
            AreIdentical = oldHash == newHash
        };

        result.IsChanged = !result.AreIdentical;

        return result;
    }

    private static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
