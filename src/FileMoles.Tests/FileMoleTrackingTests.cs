using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FileMoles;
using FileMoles.Events;
using Xunit;

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
                DebounceTime = 200 // 테스트를 위해 짧게 설정
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
            await Task.Delay(100); // Short delay for setup
            return (testDirectory, fileMole);
        }

        private async Task TearDownTestEnvironment(string testDirectory, FileMole fileMole)
        {
            fileMole.Dispose();
            await Task.Delay(200); // Short delay for cleanup
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
                fileMole.FileContentChanged += (sender, args) => contentChangedRaised = true;

                // Act
                await fileMole.EnableMoleTrackAsync(filePath);
                File.WriteAllText(filePath, "Updated content");

                // Wait for the event to be processed
                await Task.Delay(1000);

                // Assert
                Assert.True(contentChangedRaised);
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

                await fileMole.EnableMoleTrackAsync(directoryPath);

                // Act
                fileMole.AddIgnorePattern(directoryPath, "*.tmp");
                var ignorePatterns = fileMole.GetIgnorePatterns(directoryPath);

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

                await fileMole.EnableMoleTrackAsync(directoryPath);

                // Act
                fileMole.AddIncludePattern(directoryPath, "*.txt");
                var includePatterns = fileMole.GetIncludePatterns(directoryPath);

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
                await fileMole.EnableMoleTrackAsync(directoryPath);

                fileMole.AddIgnorePattern(directoryPath, "*.tmp");

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
                await fileMole.EnableMoleTrackAsync(directoryPath);

                fileMole.AddIncludePattern(directoryPath, "*.txt");

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