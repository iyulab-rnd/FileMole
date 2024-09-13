using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FileMoles.Tests;

public class TrackingTests : TestBase
{
    public TrackingTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task EnableTracking_ShouldTrackFileChanges()
    {
        var filePath = await CreateUniqueFileAsync();
        await FileMole.Tracking.EnableAsync(filePath);

        await Task.Delay(TimeSpan.FromSeconds(2)); // Wait for scan

        var contentChanged = false;
        FileMole.FileContentChanged += (sender, e) => contentChanged = true;

        await SafeFileIO.AppendAllTextAsync(filePath, "New content");

        await Task.Delay(TimeSpan.FromSeconds(2)); // Wait for debounce
        Assert.True(contentChanged);
    }

    [Fact]
    public async Task IgnorePattern_ShouldNotTrackMatchingFiles()
    {
        var filePath = await CreateUniqueFileAsync();
        FileMole.Config.AddIgnorePattern("*.tmp");
        await FileMole.Tracking.EnableAsync(TestPath);
        await FileMole.Tracking.WaitForInitialScanCompletionAsync();

        var contentChanged = false;
        FileMole.FileContentChanged += (sender, e) => contentChanged = true;

        var tmpFilePath = Path.ChangeExtension(filePath, ".tmp");
        await SafeFileIO.WriteAllTextAsync(tmpFilePath, "Temp content");

        await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for debounce
        Assert.False(contentChanged);
    }

    [Fact]
    public async Task IncludePattern_ShouldOnlyTrackMatchingFiles()
    {
        var filePath = await CreateUniqueFileAsync();
        FileMole.Config.AddIncludePattern("*.txt");
        await FileMole.Tracking.EnableAsync(TestPath);

        var contentChanged = false;
        FileMole.FileContentChanged += (sender, e) => contentChanged = true;

        await SafeFileIO.AppendAllTextAsync(filePath, "New content");

        await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for debounce
        Assert.True(contentChanged);

        contentChanged = false;
        var nonMatchingPath = Path.ChangeExtension(filePath, ".log");
        await SafeFileIO.WriteAllTextAsync(nonMatchingPath, "Log content");

        await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for debounce
        Assert.False(contentChanged);
    }

    [Fact]
    public async Task Debounce_ShouldGroupMultipleChanges()
    {
        var filePath = await CreateUniqueFileAsync();
        await FileMole.Tracking.EnableAsync(filePath);

        var changeCount = 0;
        FileMole.FileContentChanged += (sender, e) => changeCount++;

        for (int i = 0; i < 5; i++)
        {
            await SafeFileIO.AppendAllTextAsync(filePath, $"Content {i}");
            await Task.Delay(50); // Small delay between changes
        }

        await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for debounce
        Assert.Equal(1, changeCount);
    }
}