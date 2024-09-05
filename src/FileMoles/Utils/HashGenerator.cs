using System.Security.Cryptography;

namespace FileMoles.Utils;

internal class HashGenerator
{
    public async Task<string> GenerateHashAsync(string filePath)
    {
        const int maxRetries = 3;
        const int delayBetweenRetries = 100; // milliseconds

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var hash = await md5.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                await Task.Delay(delayBetweenRetries);
            }
        }

        throw new IOException($"Unable to access file: {filePath}");
    }
}
