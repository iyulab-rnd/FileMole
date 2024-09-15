using System.Security.Cryptography;

namespace FileMoles.Internal;

internal class FileHashGenerator
{
    public async Task<string> GenerateFileContentHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        const int delayBetweenRetries = 100; // milliseconds

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var md5 = MD5.Create();
                await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
                var hash = await md5.ComputeHashAsync(stream, cancellationToken);
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(delayBetweenRetries, cancellationToken);
            }
        }

        throw new IOException($"Unable to access file: {filePath}");
    }
}