using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;
using DiffPlex;

namespace FileMoles.Diff;

public class PdfDiffEntry
{
    public int PageNumber { get; set; }
    public List<TextDiffEntry> TextDiffs { get; set; } = new List<TextDiffEntry>();
}

public class PdfDiffResult : DiffResult
{
    public List<PdfDiffEntry> Entries { get; set; } = new List<PdfDiffEntry>();
}


public class PdfDiffStrategy : IDiffStrategy
{
    public async Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath, CancellationToken cancellationToken = default)
    {
        var oldText = await ExtractTextFromPdfAsync(oldFilePath);
        var newText = await ExtractTextFromPdfAsync(newFilePath);

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        var result = new PdfDiffResult { FileType = "PDF" };

        int pageNumber = 1;
        int position = 0;
        var currentPageDiffs = new List<TextDiffEntry>();

        foreach (var line in diff.Lines)
        {
            if (line.Text.StartsWith("%%% Page Break %%%"))
            {
                if (currentPageDiffs.Count > 0)
                {
                    result.Entries.Add(new PdfDiffEntry
                    {
                        PageNumber = pageNumber,
                        TextDiffs = currentPageDiffs
                    });
                }
                pageNumber++;
                currentPageDiffs = new List<TextDiffEntry>();
                position = 0;
            }
            else
            {
                var entry = new TextDiffEntry
                {
                    StartPosition = position,
                    EndPosition = position + line.Text.Length,
                    OriginalText = line.Text,
                    ModifiedText = line.Text,
                    Type = ConvertDiffType(line.Type)
                };

                currentPageDiffs.Add(entry);
                position += line.Text.Length + 1; // +1 for newline
            }
        }

        // Add the last page
        if (currentPageDiffs.Count > 0)
        {
            result.Entries.Add(new PdfDiffEntry
            {
                PageNumber = pageNumber,
                TextDiffs = currentPageDiffs
            });
        }

        result.IsChanged = result.Entries.Any(e => e.TextDiffs.Any(td => td.Type != DiffType.Unchanged));

        return result;
    }

    private async Task<string> ExtractTextFromPdfAsync(string filePath)
    {
        // Implementation depends on the PDF library you're using
        // This is a placeholder
        return await Task.FromResult("Extracted PDF text");
    }

    private DiffType ConvertDiffType(ChangeType changeType)
    {
        return changeType switch
        {
            ChangeType.Inserted => DiffType.Inserted,
            ChangeType.Deleted => DiffType.Deleted,
            ChangeType.Modified => DiffType.Modified,
            _ => DiffType.Unchanged,
        };
    }
}