using Moq;
using FileMoles.Storage;

namespace FileMoles.Tests
{
    public class FileMoleFixture : IDisposable
    {
        public string TestDir { get; }
        public Mock<IStorageProvider> MockStorageProvider { get; }

        public FileMoleFixture()
        {
            TestDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(TestDir);
            MockStorageProvider = new Mock<IStorageProvider>();
        }

        public void Dispose()
        {
            const int maxRetries = 3;
            const int delayMs = 100;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (Directory.Exists(TestDir))
                    {
                        Directory.Delete(TestDir, true);
                    }
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (i == maxRetries - 1)
                    {
                        Console.WriteLine($"Warning: Failed to delete directory {TestDir}. Error: {ex.Message}");
                    }
                    else
                    {
                        Thread.Sleep(delayMs);
                    }
                }
                catch (IOException ex)
                {
                    if (i == maxRetries - 1)
                    {
                        Console.WriteLine($"Warning: Failed to delete directory {TestDir}. Error: {ex.Message}");
                    }
                    else
                    {
                        Thread.Sleep(delayMs);
                    }
                }
            }

            GC.SuppressFinalize(this);
        }
    }
}