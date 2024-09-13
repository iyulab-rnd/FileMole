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

        await FileMole.Tracking.EnableAsync(TestPath);
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Arrange
        var file = Path.Combine(TestPath, "test.txt");
        File.WriteAllText(file, "Hello, World!");
        await Task.Delay(TimeSpan.FromSeconds(2));

        File.WriteAllText(file, "Hello, World! Changed");
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(2, changed);
    }
}
