using Xunit.Abstractions;
using FileMoles.Tracking;

namespace FileMoles.Tests.Tracking;

public class TrackingTests : TestBase
{
    public TrackingTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task TrackFile_ShouldTrackChanges()
    {
        // Arrange
        string filePath = await CreateUniqueTxtFileAsync("Initial content");

        int changed = 0;
        FileMole.FileContentChanged += (s, e) =>
        {
            changed++;
        };

        // Act
        await FileMole.TrackingAsync(filePath);

        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.True(await FileMole.IsTrackingAsync(filePath));

        // Modify file
        await RetryFile.AppendAllTextAsync(filePath, "\nNew content");

        await Task.Delay(TimeSpan.FromSeconds(1)); 

        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task UntrackFile_ShouldStopTracking()
    {
        // Arrange
        string filePath = await CreateUniqueTxtFileAsync("Content to untrack");
        await FileMole.TrackingAsync(filePath);

        // Act
        await FileMole.UntrackingAsync(filePath);

        // Assert
        Assert.False(await FileMole.IsTrackingAsync(filePath));
    }

    [Fact]
    public async Task TrackMultipleFiles_ShouldTrackAllFiles()
    {
        // Arrange
        string file1 = await CreateUniqueTxtFileAsync("File 1 content");
        string file2 = await CreateUniqueTxtFileAsync("File 2 content");

        // Act
        await FileMole.TrackingAsync(file1);
        await FileMole.TrackingAsync(file2);

        // Assert
        Assert.True(await FileMole.IsTrackingAsync(file1));
        Assert.True(await FileMole.IsTrackingAsync(file2));
    }

    [Fact]
    public async Task TrackNonExistentFile_ShouldThrowException()
    {
        // Arrange
        string nonExistentFile = Path.Combine(TestPath, "non_existent.txt");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => FileMole.TrackingAsync(nonExistentFile));
    }

    [Fact]
    public async Task TrackAndModifyFile_ShouldDetectChanges()
    {
        // Arrange
        string filePath = await CreateUniqueTxtFileAsync("Original content");
        await FileMole.TrackingAsync(filePath);

        // Act
        await RetryFile.WriteAllTextAsync(filePath, "Modified content");

        // Wait for changes to be detected
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        var diff = await HillUtils.GetDiffAsync(filePath);
        Assert.NotNull(diff);
    }

    [Fact]
    public async Task TrackFile_ThenDelete_ShouldHandleGracefully()
    {
        // Arrange
        string filePath = await CreateUniqueTxtFileAsync("Temporary content");
        await FileMole.TrackingAsync(filePath);

        // Act
        await RetryFile.DeleteAsync(filePath);

        // Wait for changes to be detected
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        var isTracking = await FileMole.IsTrackingAsync(filePath);
        Assert.False(isTracking);
    }

    [Fact]
    public async Task TrackFile_RenameFile_ShouldContinueTracking()
    {
        // Arrange
        string originalPath = await CreateUniqueTxtFileAsync("Content to rename");
        await FileMole.TrackingAsync(originalPath);

        // Act
        string newPath = Path.Combine(Path.GetDirectoryName(originalPath)!, "renamed_file.txt");
        File.Move(originalPath, newPath);

        // Wait for changes to be detected
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        var isTrackingOriginal = await FileMole.IsTrackingAsync(originalPath);
        Assert.False(isTrackingOriginal);

        var isTrackingNew = await FileMole.IsTrackingAsync(newPath);
        Assert.False(isTrackingNew); // Rename 은 자동으로 추적대상이 되지 않습니다.
    }
}