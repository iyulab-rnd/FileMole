using FileMoles.Diff;
using System.IO;
using FileMoles.Internals;

namespace FileMoles.Diff;

public class DiffStrategyFactory
{
    public static IDiffStrategy CreateStrategy(string filePath)
    {
        if (FileMoleUtils.IsTextFile(filePath))
        {
            return new TextDiffStrategy();
        }

        var extension = Path.GetExtension(filePath).ToLower();
        return extension switch
        {
            ".pdf" => new PdfDiffStrategy(),
            ".docx" or ".xlsx" => new OdfDiffStrategy(),
            _ => new BinaryDiffStrategy()
        };
    }
}