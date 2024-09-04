using Xunit;
using Xunit.Abstractions;
using FileMoles.Diff;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

namespace FileMoles.Tests
{
    public class FileMoleTrackTests : IClassFixture<FileMoleFixture>, IDisposable
    {
        private readonly string _tempPath;
        private readonly FileMole _fileMole;
        private readonly ITestOutputHelper _output;
        private readonly FileMoleFixture _fixture;

        public FileMoleTrackTests(ITestOutputHelper output, FileMoleFixture fixture)
        {
            _output = output;
            _fixture = fixture;
            _tempPath = fixture.TestDir;
            var options = new FileMoleOptions
            {
                DebounceTime = 100
            };
            _fileMole = new FileMoleBuilder()
                .SetOptions(options)
                .AddMole(_tempPath)
                .Build();
            _fileMole.EnableMoleTrack(_tempPath);
        }

        private async Task<string> CreateUniqueFileAsync(string content = "Test content")
        {
            string filePath = Path.Combine(_tempPath, Guid.NewGuid().ToString() + ".txt");
            await File.WriteAllTextAsync(filePath, content);
            return filePath;
        }

        private async Task WaitForEventProcessingAsync()
        {
            await Task.Delay(1000);
        }

        [Fact]
        public async Task TrackAndCompareFile_ShouldTrackAndCalculateDiff()
        {
            var filePath = await CreateUniqueFileAsync("Original content");
            await WaitForEventProcessingAsync();

            DiffResult? diff = null;
            var diffCalculated = new TaskCompletionSource<bool>();

            void OnMoleTrackChanged(object? sender, FileContentChangedEventArgs e)
            {
                if (e.FullPath == filePath)
                {
                    diff = e.Diff;
                    diffCalculated.SetResult(true);
                }
            }

            _fileMole.FileContentChanged += OnMoleTrackChanged;

            try
            {
                // Modify the file content to trigger diff calculation
                await File.WriteAllTextAsync(filePath, "Modified content");
                await WaitForEventProcessingAsync();

                // Wait for the diff to be calculated with a timeout
                await Task.WhenAny(diffCalculated.Task, Task.Delay(5000));

                Assert.NotNull(diff);
                Assert.IsType<TextDiffResult>(diff);

                var textDiff = diff as TextDiffResult;
                Assert.NotNull(textDiff);
                Assert.Equal("Text", textDiff.FileType);

                var entries = textDiff.Entries;
                Assert.NotEmpty(entries);

                Assert.Contains(entries, e => e.Type == DiffType.Deleted && e.OriginalText == "Original content");
                Assert.Contains(entries, e => e.Type == DiffType.Inserted && e.ModifiedText == "Modified content");
            }
            finally
            {
                _fileMole.FileContentChanged -= OnMoleTrackChanged;
            }
        }

        public void Dispose()
        {
            _fileMole.Dispose();
        }
    }
}