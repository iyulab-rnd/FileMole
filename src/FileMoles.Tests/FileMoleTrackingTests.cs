namespace FileMoles.Tests
{
    public class FileMoleTrackingTests
    {
        private FileMole CreateFileMole(string testDirectory)
        {
            var options = new FileMoleOptions
            {
                DataPath = Path.Combine(testDirectory, "FileMoleData"),
                Moles = new List<Mole> { new Mole { Path = testDirectory, Type = MoleType.Local } },
                DebounceTime = 100 // 더 짧은 디바운스 시간으로 설정
            };

            return new FileMoleBuilder()
                .SetOptions(options)
                .Build();
        }

        private async Task<(string directory, FileMole mole)> SetupTestEnvironment()
        {
            var testDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(testDirectory);
            var fileMole = CreateFileMole(testDirectory);
            await Task.Delay(500); // 초기 설정을 위한 대기 시간 증가
            return (testDirectory, fileMole);
        }

        private async Task TearDownTestEnvironment(string testDirectory, FileMole fileMole)
        {
            fileMole.Dispose();
            await Task.Delay(500); // 정리를 위한 대기 시간 증가
            FileSafe.DeleteRetry(testDirectory);
        }

        [Fact]
        public async Task EnableMoleTrack_ShouldTrackFileChanges()
        {
            var (testDir, fileMole) = await SetupTestEnvironment();

            try
            {
                // Arrange
                var filePath = Path.Combine(testDir, "tracktest.txt");
                File.WriteAllText(filePath, "Initial content");

                var contentChangedRaised = false;
                fileMole.FileContentChanged += (sender, args) =>
                {
                    contentChangedRaised = true;
                    Console.WriteLine("FileContentChanged event raised");
                };

                await fileMole.Tracking.EnableAsync(filePath);
                Console.WriteLine($"Tracking enabled for {filePath}");

                // Act
                File.WriteAllText(filePath, "Updated content");
                Console.WriteLine($"File content updated: {filePath}");

                // Wait for the event to be processed
                for (int i = 0; i < 30 && !contentChangedRaised; i++)
                {
                    await Task.Delay(100);
                }

                // Assert
                Assert.True(contentChangedRaised, "FileContentChanged event was not raised");
            }
            finally
            {
                await TearDownTestEnvironment(testDir, fileMole);
            }
        }

        [Fact]
        public async Task AddIgnorePattern_ShouldIgnoreMatchingFiles()
        {
            var (testDir, fileMole) = await SetupTestEnvironment();

            try
            {
                // Arrange
                var directoryPath = Path.Combine(testDir, "ignoretest");
                Directory.CreateDirectory(directoryPath);

                await fileMole.Tracking.EnableAsync(directoryPath);

                // Act
                fileMole.Config.AddIgnorePattern("*.tmp");
                var ignorePatterns = fileMole.Config.GetIgnorePatterns();

                // Assert
                Assert.Contains("*.tmp", ignorePatterns);
            }
            finally
            {
                await TearDownTestEnvironment(testDir, fileMole);
            }
        }

        [Fact]
        public async Task AddIncludePattern_ShouldIncludeMatchingFiles()
        {
            var (testDir, fileMole) = await SetupTestEnvironment();

            try
            {
                // Arrange
                var directoryPath = Path.Combine(testDir, "includetest");
                Directory.CreateDirectory(directoryPath);

                await fileMole.Tracking.EnableAsync(directoryPath);

                // Act
                fileMole.Config.AddIncludePattern("*.txt");
                var includePatterns = fileMole.Config.GetIncludePatterns();

                // Assert
                Assert.Contains("*.txt", includePatterns);
            }
            finally
            {
                await TearDownTestEnvironment(testDir, fileMole);
            }
        }

        [Fact]
        public async Task EnableMoleTrack_ShouldRespectIgnorePatterns()
        {
            var (testDir, fileMole) = await SetupTestEnvironment();

            try
            {
                // Arrange
                var directoryPath = Path.Combine(testDir, "ignoretest");
                Directory.CreateDirectory(directoryPath);
                await fileMole.Tracking.EnableAsync(directoryPath);

                fileMole.Config.AddIgnorePattern("*.tmp");

                var ignoredFilePath = Path.Combine(directoryPath, "ignored.tmp");
                var trackedFilePath = Path.Combine(directoryPath, "tracked.txt");

                var contentChangedCount = 0;
                fileMole.FileContentChanged += (sender, args) => contentChangedCount++;

                // Act
                File.WriteAllText(ignoredFilePath, "Ignored content");
                File.WriteAllText(trackedFilePath, "Tracked content");

                // Wait for events to be processed
                await Task.Delay(1000);

                // Assert
                Assert.Equal(1, contentChangedCount); // Only the tracked file should trigger the event
            }
            finally
            {
                await TearDownTestEnvironment(testDir, fileMole);
            }
        }

        [Fact]
        public async Task EnableMoleTrack_ShouldRespectIncludePatterns()
        {
            var (testDir, fileMole) = await SetupTestEnvironment();

            try
            {
                // Arrange
                var directoryPath = Path.Combine(testDir, "includetest");
                Directory.CreateDirectory(directoryPath);
                await fileMole.Tracking.EnableAsync(directoryPath);

                fileMole.Config.AddIncludePattern("*.txt");

                var includedFilePath = Path.Combine(directoryPath, "included.txt");
                var notIncludedFilePath = Path.Combine(directoryPath, "notincluded.tmp");

                var contentChangedCount = 0;
                fileMole.FileContentChanged += (sender, args) => contentChangedCount++;

                // Act
                File.WriteAllText(includedFilePath, "Included content");
                File.WriteAllText(notIncludedFilePath, "Not included content");

                // Wait for events to be processed
                await Task.Delay(1000);

                // Assert
                Assert.Equal(1, contentChangedCount); // Only the included file should trigger the event
            }
            finally
            {
                await TearDownTestEnvironment(testDir, fileMole);
            }
        }
    }
}