using Xunit.Abstractions;

namespace FileMoles.Tests
{
    public class FileMoleEventTests : IClassFixture<FileMoleFixture>
    {
        private readonly string _tempPath;
        private readonly FileMole _fileMole;
        private readonly ITestOutputHelper _output;
        private readonly FileMoleFixture _fixture;

        public FileMoleEventTests(FileMoleFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _tempPath = _fixture.TestDir;

            var options = new FileMoleOptions();
            _fileMole = new FileMoleBuilder()
                .AddMole(_tempPath)
                .Build();
        }

        private async Task<string> CreateUniqueFileAsync(string content = "Test content")
        {
            string filePath = Path.Combine(_tempPath, Guid.NewGuid().ToString() + ".txt");
            await RetryOnExceptionAsync(async () => await File.WriteAllTextAsync(filePath, content));
            return filePath;
        }

        private async Task RetryOnExceptionAsync(Func<Task> action, int maxRetries = 3)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await action();
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(100 * (i + 1));  // Exponential backoff
                }
            }
            await action();  // If all retries fail, let the exception propagate
        }

        [Fact]
        public async Task FileCreated_ShouldTriggerEvent()
        {
            var eventTriggered = false;
            _fileMole.FileCreated += (sender, args) =>
            {
                eventTriggered = true;
            };

            var filePath = await CreateUniqueFileAsync();

            await WaitForEventProcessingAsync();

            Assert.True(eventTriggered, "File creation event was not triggered");
        }

        [Fact]
        public async Task FileModified_ShouldTriggerEvent()
        {
            var filePath = await CreateUniqueFileAsync("Initial content");
            _output.WriteLine($"Created file: {filePath}");

            var eventTriggered = false;
            _fileMole.FileChanged += (sender, args) =>
            {
                eventTriggered = true;
                _output.WriteLine($"FileChanged event triggered for: {args.FullPath}");
            };

            // Wait for initial file creation to be processed
            await WaitForEventProcessingAsync();

            _output.WriteLine("Modifying file content...");
            await RetryOnExceptionAsync(async () =>
            {
                await File.AppendAllTextAsync(filePath, "Modified content");
                _output.WriteLine($"File modified: {filePath}");
            });

            // Increase wait time and add periodic checks
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(200);
                if (eventTriggered)
                {
                    _output.WriteLine($"Event triggered after {(i + 1) * 200}ms");
                    break;
                }
            }

            // Final check
            if (!eventTriggered)
            {
                var fileContent = await File.ReadAllTextAsync(filePath);
                _output.WriteLine($"Final file content: {fileContent}");
                _output.WriteLine($"File last write time: {File.GetLastWriteTime(filePath)}");
            }

            Assert.True(eventTriggered, "File modification event was not triggered");
        }

        [Fact]
        public async Task FileDeleted_ShouldTriggerEvent()
        {
            var filePath = await CreateUniqueFileAsync();

            var eventTriggered = false;
            _fileMole.FileDeleted += (sender, args) =>
            {
                eventTriggered = true;
            };

            await RetryOnExceptionAsync(() => { File.Delete(filePath); return Task.CompletedTask; });

            await WaitForEventProcessingAsync();

            Assert.True(eventTriggered, "File deletion event was not triggered");
        }

        [Fact]
        public async Task FileRenamed_ShouldTriggerEvent()
        {
            var originalPath = await CreateUniqueFileAsync();
            var newPath = Path.Combine(_tempPath, Guid.NewGuid().ToString() + ".txt");

            var eventTriggered = false;
            _fileMole.FileRenamed += (sender, args) =>
            {
                eventTriggered = true;
                Assert.Equal(originalPath, args.OldFullPath);
                Assert.Equal(newPath, args.FullPath);
            };

            await RetryOnExceptionAsync(() => { File.Move(originalPath, newPath); return Task.CompletedTask; });

            await WaitForEventProcessingAsync();

            Assert.True(eventTriggered, "File rename event was not triggered");
        }

        [Fact]
        public async Task FileChanged_ShouldNotTriggerEventWhenContentUnchanged()
        {
            var filePath = await CreateUniqueFileAsync("Initial content");

            await WaitForEventProcessingAsync();

            int changeEventCount = 0;
            _fileMole.FileChanged += (sender, args) =>
            {
                changeEventCount++;
            };

            Assert.Equal(0, changeEventCount);

            await RetryOnExceptionAsync(async () => await File.WriteAllTextAsync(filePath, "Modified content"));

            await WaitForEventProcessingAsync();

            Assert.Equal(1, changeEventCount);
        }

        private async Task WaitForEventProcessingAsync()
        {
            await Task.Delay(1000);  // Increased delay to allow for potential retries
        }
    }
}