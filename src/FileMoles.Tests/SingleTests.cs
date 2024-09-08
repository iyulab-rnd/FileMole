namespace FileMoles.Tests;

public class SingleTests
{
    private FileMole CreateFileMole(string testDirectory)
    {
        var options = new FileMoleOptions
        {
            DataPath = Path.Combine(testDirectory),
            Moles = [new Mole { Path = testDirectory, Type = MoleType.Local }],
            DebounceTime = 200,
        };

        return new FileMoleBuilder()
            .SetOptions(options)
            .Build();
    }

    private async Task<(string directory, FileMole mole)> SetupTestEnvironment()
    {
        var basePath = Path.Combine("c:", "file-mole-tests");
        await FileSafe.DeleteRetryAsync(basePath);

        var testDirectory = Path.Combine("c:", "file-mole-tests", basePath);
        Directory.CreateDirectory(testDirectory);
        var fileMole = CreateFileMole(testDirectory);
        await Task.Delay(100); // Short delay for setup
        return (testDirectory, fileMole);
    }

    private async Task TearDownTestEnvironment(string testDirectory, FileMole fileMole)
    {
        await fileMole.DisposeAsync();
        try
        {
            await FileSafe.DeleteRetryAsync(testDirectory);
        }
        catch (Exception)
        {
        }
    }

    [Fact]
    public async Task BackupFilesTests()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            var changed = 0;
            fileMole.FileContentChanged += (s, e) =>
            {
                changed++;
            };

            await fileMole.Tracking.EnableAsync(testDir);
            await Task.Delay(TimeSpan.FromSeconds(1));

            // Arrange
            var file = Path.Combine(testDir, "test.txt");
            File.WriteAllText(file, "Hello, World!");
            await Task.Delay(TimeSpan.FromSeconds(2));

            File.WriteAllText(file, "Hello, World! Changed");
            await Task.Delay(TimeSpan.FromSeconds(2));

            // Assert
            Assert.Equal(2, changed);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

}
