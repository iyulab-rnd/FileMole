using Xunit.Abstractions;

namespace FileMoles.Tests;

[Collection("FileMole Tests")]
public class FeatureTests : TestBase
{
    public FeatureTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task BackupFilesTests()
    {
        var changed = 0;
        FileMole.FileContentChanged += (s, e) =>
        {
            changed++;
        };

        // Arrange
        var testFile = Path.Combine(TestPath, "test.txt");
        File.WriteAllText(testFile, "Hello, World! First");
        await FileMole.Tracking.EnableAsync(testFile);

        File.WriteAllText(testFile, "Hello, World! Changed 1");
        await Task.Delay(TimeSpan.FromSeconds(2));

        File.WriteAllText(testFile, "Hello, World! Changed 2");
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(2, changed);
    }
}
