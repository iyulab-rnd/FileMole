using FileMoles.Internal;

namespace FileMoles.Tracking;

public class TrackingIgnoreManager : IgnoreManager
{
    protected TrackingIgnoreManager(string ignoreFilePath) : base(ignoreFilePath)
    {
    }

    protected override string GetDefaultIgnoreContent()
    {
        return @"# All Ignore
*
";
    }

    public override bool IsIgnored(string filePath)
    {
        if (IOHelper.IsHidden(filePath)) return true;

        return base.IsIgnored(filePath);
    }

    public Task IncludeTextFormatsAsync()
    {
        var ignoreLines = @"
# Text base Formats
!*.txt
!*.md

# Document Formats
!*.docx
!*.xlsx
!*.pptx
!*.pdf
";
        return AddRulesAsync(ignoreLines);
    }

    public Task IncludeFilePathAsync(string filePath)
    {
        var relativePath = Path.GetRelativePath(_rootDirectory, filePath);
        var ignoreLine = $"!{relativePath}";

        return AddRulesAsync(ignoreLine);
    }

    internal Task ExcludeFilePathAsync(string filePath)
    {
        var relativePath = Path.GetRelativePath(_rootDirectory, filePath);
        var ignoreLine = $"!{relativePath}";

        return RemoveRulesAsync(ignoreLine);
    }

    new public static async Task<TrackingIgnoreManager> CreateAsync(string ignoreFilePath)
    {
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        await manager.InitializeAsync();
        return manager;
    }

    new public static TrackingIgnoreManager CreateNew(string ignoreFilePath)
    {
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        _ = manager.InitializeAsync();
        Task.Delay(300).Wait();
        return manager;
    }
}