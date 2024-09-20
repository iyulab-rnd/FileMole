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
            output.WriteLine(e.Diff.ToString());
            changed++;
        };

        // Arrange
        var testFile = Path.Combine(TestPath, "test.txt");
        File.WriteAllText(testFile, "Hello, World! First");
        await FileMole.TrackingAsync(testFile);
        await Task.Delay(TimeSpan.FromSeconds(1));

        File.WriteAllText(testFile, "Hello, World! Changed 1");
        await Task.Delay(TimeSpan.FromSeconds(1));

        File.WriteAllText(testFile, "Hello, World! Changed 2");
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(2, changed);
    }
}
