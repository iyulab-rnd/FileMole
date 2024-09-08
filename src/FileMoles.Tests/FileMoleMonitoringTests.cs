namespace FileMoles.Tests;

public class FileMoleMonitoringTests
{
    private FileMole CreateFileMole(string testDirectory)
    {
        var options = new FileMoleOptions
        {
            DataPath = Path.Combine(testDirectory, "FileMoleData"),
            Moles = [new() { Path = testDirectory, Type = MoleType.Local }]
        };

        return new FileMoleBuilder()
            .SetOptions(options)
            .Build();
    }

    private async Task<(string directory, FileMole mole)> SetupTestEnvironment()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "FileMoleMonitoringTests", Path.GetRandomFileName());
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
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, true);
            }
        }
        catch (Exception)
        {
        }
    }

    [Fact]
    public async Task AddMole_ShouldWatchDirectory()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var newDirectory = Path.Combine(testDir, "NewMoleDirectory");
            Directory.CreateDirectory(newDirectory);

            // Act
            await fileMole.AddMoleAsync(newDirectory, MoleType.Local);

            // Assert
            Assert.Contains(fileMole.GetMoles(), m => m.Path == newDirectory && m.Type == MoleType.Local);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task FileCreated_ShouldTriggerEvent()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();
        try
        {
            // Arrange
            var eventRaised = false;
            fileMole.FileCreated += (sender, args) => eventRaised = true;

            // Act
            var filePath = Path.Combine(testDir, "testfile.txt");
            File.WriteAllText(filePath, "Test content");

            // Wait for the event to be processed
            await Task.Delay(1000);

            // Assert
            Assert.True(eventRaised);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task SearchFiles_ShouldReturnMatchingFiles()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var filePath = Path.Combine(testDir, "searchtest.txt");
            File.WriteAllText(filePath, "Test content");

            // Wait for indexing
            await Task.Delay(1000);

            // Act
            var results = new List<FileInfo>();
            await foreach (var item in fileMole.SearchFilesAsync("searchtest"))
            {
                results.Add(item);
            }

            // Assert
            Assert.Single(results);
            Assert.Equal(filePath, results.First().FullName);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task GetFileCount_ShouldReturnCorrectCount()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var filePath1 = Path.Combine(testDir, "counttest1.txt");
            var filePath2 = Path.Combine(testDir, "counttest2.txt");
            File.WriteAllText(filePath1, "Test content 1");
            File.WriteAllText(filePath2, "Test content 2");

            // Wait for indexing
            await Task.Delay(1000);

            // Act
            var count = await fileMole.GetFileCountAsync(testDir);

            // Assert
            Assert.Equal(2, count);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task FileChanged_ShouldTriggerEvent()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var filePath = Path.Combine(testDir, "changetest.txt");
            File.WriteAllText(filePath, "Initial content");

            var eventRaised = false;
            fileMole.FileChanged += (sender, args) => eventRaised = true;

            // Wait for initial indexing
            await Task.Delay(1000);

            // Act
            File.WriteAllText(filePath, "Updated content");

            // Wait for the event to be processed
            await Task.Delay(1000);

            // Assert
            Assert.True(eventRaised);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task FileDeleted_ShouldTriggerEvent()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var filePath = Path.Combine(testDir, "deletetest.txt");
            File.WriteAllText(filePath, "Test content");

            var eventRaised = false;
            fileMole.FileDeleted += (sender, args) => eventRaised = true;

            // Wait for initial indexing
            await Task.Delay(1000);

            // Act
            File.Delete(filePath);

            // Wait for the event to be processed
            await Task.Delay(1000);

            // Assert
            Assert.True(eventRaised);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task ConfigManager_ShouldTrackFile()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var filePath = Path.Combine(testDir, "configtest.txt");
            File.WriteAllText(filePath, "Test content");

            // Act
            bool shouldTrack = fileMole.Config.ShouldTrackFile(filePath);

            // Assert
            Assert.True(shouldTrack);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }

    [Fact]
    public async Task TrackingManager_ShouldTrackFile()
    {
        var (testDir, fileMole) = await SetupTestEnvironment();

        try
        {
            // Arrange
            var filePath = Path.Combine(testDir, "trackingtest.txt");
            File.WriteAllText(filePath, "Test content");

            // Act
            await fileMole.Tracking.EnableAsync(filePath);
            bool isTracked = fileMole.Config.ShouldTrackFile(filePath);

            // Assert
            Assert.True(isTracked);
        }
        finally
        {
            await TearDownTestEnvironment(testDir, fileMole);
        }
    }
}