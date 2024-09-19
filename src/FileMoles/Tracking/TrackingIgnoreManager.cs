using FileMoles.Internal;

namespace FileMoles.Tracking;
#if DEBUG
public
#else
internal 
#endif
    class TrackingIgnoreManager(string ignoreFilePath) : IgnoreManager(ignoreFilePath)
{
    protected override string GetDefaultIgnoreContent()
    {
        return @"# All Ignore
*
";
    }

    public void IncludeTextFormat()
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
        AddRules(ignoreLines);
    }

    public void IncludeFilePath(string filePath)
    {
        var relativePath = Path.GetRelativePath(_rootDirectory, filePath);
        var ignoreLine = $"!{relativePath}";

        AddRules(ignoreLine);
    }
}