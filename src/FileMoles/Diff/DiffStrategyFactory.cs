using FileMoles.Diff;
using System.IO;
using FileMoles.Internal;

namespace FileMoles.Diff;

public class DiffStrategyFactory
{
    public static IDiffStrategy CreateStrategy(string filePath)
    {
        if (FileMoleHelper.IsTextFile(filePath))
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