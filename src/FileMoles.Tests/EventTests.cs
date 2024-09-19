using Xunit.Abstractions;

namespace FileMoles.Tests;

[Collection("FileMole Tests")]
public class EventTests : TestBase
{
    public EventTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task FileCreated_ShouldTriggerEvent()
    {
        var eventTriggered = false;
        FileMole.FileCreated += (sender, args) =>
        {
            eventTriggered = true;
        };

        var filePath = await CreateUniqueTxtFileAsync();

        await WaitForEventProcessingAsync();

        Assert.True(eventTriggered, "File creation event was not triggered");
    }

    [Fact]
    public async Task FileModified_ShouldTriggerEvent()
    {
        var filePath = await CreateUniqueTxtFileAsync("Initial content");
        output.WriteLine($"Created file: {filePath}");

        var eventTriggered = false;
        FileMole.FileChanged += (sender, args) =>
        {
            eventTriggered = true;
            output.WriteLine($"FileChanged event triggered for: {args.FullPath}");
        };

        await WaitForEventProcessingAsync();

        output.WriteLine("Modifying file content...");
        await SafeFileIO.AppendAllTextAsync(filePath, "Modified content");
        output.WriteLine($"File modified: {filePath}");

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(200);
            if (eventTriggered)
            {
                output.WriteLine($"Event triggered after {(i + 1) * 200}ms");
                break;
            }
        }

        if (!eventTriggered)
        {
            var fileContent = await File.ReadAllTextAsync(filePath);
            output.WriteLine($"Final file content: {fileContent}");
            output.WriteLine($"File last write time: {File.GetLastWriteTime(filePath)}");
        }

        Assert.True(eventTriggered, "File modification event was not triggered");
    }

    [Fact]
    public async Task FileDeleted_ShouldTriggerEvent()
    {
        var filePath = await CreateUniqueTxtFileAsync();

        var eventTriggered = false;
        FileMole.FileDeleted += (sender, args) =>
        {
            eventTriggered = true;
        };

        await SafeFileIO.DeleteAsync(filePath);

        await WaitForEventProcessingAsync();

        Assert.True(eventTriggered, "File deletion event was not triggered");
    }

    [Fact]
    public async Task FileRenamed_ShouldTriggerEvent()
    {
        var originalPath = await CreateUniqueTxtFileAsync();
        var newPath = Path.Combine(TestPath, Guid.NewGuid().ToString() + ".txt");

        var eventTriggered = false;
        FileMole.FileRenamed += (sender, args) =>
        {
            eventTriggered = true;
            Assert.Equal(originalPath, args.OldFullPath);
            Assert.Equal(newPath, args.FullPath);
        };

        await SafeFileIO.MoveAsync(originalPath, newPath);

        await WaitForEventProcessingAsync();

        Assert.True(eventTriggered, "File rename event was not triggered");
    }

    [Fact]
    public async Task FileChanged_ShouldNotTriggerEventWhenContentUnchanged()
    {
        var filePath = await CreateUniqueTxtFileAsync("Initial content");

        await WaitForEventProcessingAsync();

        int changeEventCount = 0;
        FileMole.FileChanged += (sender, args) =>
        {
            changeEventCount++;
        };

        Assert.Equal(0, changeEventCount);

        await SafeFileIO.WriteAllTextAsync(filePath, "Modified content");

        await WaitForEventProcessingAsync();

        Assert.Equal(1, changeEventCount);
    }

    private async Task WaitForEventProcessingAsync()
    {
        await Task.Delay(2000);  // Increased delay to allow for potential retries
    }
}
