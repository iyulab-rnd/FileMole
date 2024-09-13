using Xunit.Abstractions;

namespace FileMoles.Tests;

public abstract class TestBase : IDisposable
{
    protected readonly ITestOutputHelper output;

    public string TestPath { get; set; }
    public string DataPath { get; set; }
    public FileMole FileMole { get; }

    public TestBase(ITestOutputHelper output)
    {
        this.output = output;

        // 고유한 임시 디렉터리 생성
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        TestPath = tempPath;
        DataPath = tempPath;

        FileMole = BuildFileMole();
    }

    private FileMole BuildFileMole()
    {
        var options = new FileMoleOptions()
        {
            DataPath = TestPath,
            DebounceTime = 200, // 200ms
        };
        return new FileMoleBuilder()
            .SetOptions(options)
            .AddMole(TestPath)
            .Build();
    }

    protected async Task<string> CreateUniqueFileAsync(string content = "test")
    {
        string filePath = Path.Combine(TestPath, Guid.NewGuid().ToString() + ".txt");
        await SafeFileIO.WriteAllTextAsync(filePath, content);
        return filePath;
    }

    public void Dispose()
    {
        FileMole.Dispose();
        try
        {
            SafeFileIO.DeleteAsync(TestPath).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
        }

        GC.SuppressFinalize(this);
    }
}