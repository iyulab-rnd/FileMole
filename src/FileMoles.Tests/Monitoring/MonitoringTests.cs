using Xunit.Abstractions;

namespace FileMoles.Tests.Monitoring;

public class MonitoringTests(ITestOutputHelper output) : TestBase(output)
{
    [Fact]
    public async Task FileCreated_ShouldTriggerEvent()
    {
        var eventTriggered = false;
        FileMole.FileCreated += (sender, e) => eventTriggered = true;

        await CreateUniqueTxtFileAsync();

        await Task.Delay(700); // Wait for event to be processed
        Assert.True(eventTriggered);
    }

    [Fact]
    public async Task FileChanged_ShouldTriggerEvent()
    {
        var filePath = await CreateUniqueTxtFileAsync();
        var eventTriggered = false;
        FileMole.FileChanged += (sender, e) => eventTriggered = true;

        await RetryFile.AppendAllTextAsync(filePath, "New content");

        await Task.Delay(700); // Wait for event to be processed
        Assert.True(eventTriggered);
    }

    [Fact]
    public async Task FileDeleted_ShouldTriggerEvent()
    {
        var filePath = await CreateUniqueTxtFileAsync();
        var eventTriggered = false;
        FileMole.FileDeleted += (sender, e) => eventTriggered = true;

        await RetryFile.DeleteAsync(filePath);

        await Task.Delay(700); // Wait for event to be processed
        Assert.True(eventTriggered);
    }
}